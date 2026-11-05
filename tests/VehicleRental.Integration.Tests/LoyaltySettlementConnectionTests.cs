using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Application.Loyalty;
using Car_Rental_System_BE.Infrastructure.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Loyalty;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VehicleRental.Tests.Phase0;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Phase 2 / D-4 (connection plumbing, not pricing): every loyalty read performed inside a settlement must run on
/// the settlement's own connection and transaction instead of taking a second connection out of the pool.
///
/// Two consequences are covered here, both through the real production path
/// (<see cref="RentalLifecycleService.CompleteAsync"/> -> <see cref="NpgsqlRentalLifecycleRepository"/> ->
/// <see cref="LoyaltyAwardAdapter"/> -> <see cref="NpgsqlLoyaltyRepository"/>, real PostgreSQL, no mocks):
/// <list type="number">
/// <item>STALE READ: a read in the settlement cannot see rows written earlier in the same transaction, so the
/// tier - and therefore the multiplier - is computed from data that is one step behind.</item>
/// <item>POOL PRESSURE: each settlement holds two pool connections instead of one, so a pool of N can deadlock
/// at exactly N simultaneous settlements.</item>
/// </list>
/// Nothing here asserts a new points rule: the expected values are exactly what
/// <see cref="Car_Rental_System_BE.Domain.Loyalty.LoyaltyDomainRules.CalculateEarn"/> already computes.
/// </summary>
public sealed class LoyaltySettlementConnectionTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private const string ProbeApplicationName = "loyalty-settlement-probe";

    /// <summary>
    /// 480 committed earned points (Silver starts at 500) + one 400,000 VND award of 40 base points:
    /// first award reads 480 -> Member (1.00x) -> 40 points, lifetime becomes 520;
    /// a read that joins the settlement transaction sees 520 -> Silver (1.10x) -> floor(40 * 1.10) = 44 points;
    /// a read that does NOT join it still sees 480 -> Member -> 40 points.
    /// </summary>
    [Fact]
    public async Task A_loyalty_read_inside_the_settlement_sees_points_written_earlier_in_the_same_transaction()
    {
        var returnedAt = Phase0Harness.ScheduledEnd;
        var rental = await Phase0Harness.ReadyDirectRentalAsync(db.DataSource, returnedAt, depositPrice: 0m);

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason, created_by)
            VALUES($1, 'earn', 480, 'Committed history before the settlement', 'system')
            """, rental.CustomerId);

        // The second award needs its own rental id: ux_clt_ref allows one earn row per (type, ref_type, ref_id).
        var otherVehicleId = await Seed.VehicleAsync(db.DataSource);
        var otherRentalId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rentals(customer_id, vehicle_id, rental_type, status, vat_price, deposit_price, total_price)
            VALUES($1, $2, 'hourly', 'completed', 0, 0, 0)
            RETURNING id
            """, rental.CustomerId, otherVehicleId))!;

        var clock = new SettableTimeProvider(returnedAt);
        var loyaltyRepo = new NpgsqlLoyaltyRepository(db.DataSource, clock);
        var realAdapter = new LoyaltyAwardAdapter(loyaltyRepo, NullLogger<LoyaltyAwardAdapter>.Instance, new LoyaltyOptions());
        var port = new SecondAwardInSameSettlementPort(realAdapter, otherRentalId);

        var service = Phase0Harness.Service(db.DataSource, clock, loyaltyAward: port);
        await service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None);

        Assert.Equal(2, port.Awards);

        var settledAward = await EarnRowAsync(rental.CustomerId, rental.RentalId);
        var readBackAward = await EarnRowAsync(rental.CustomerId, otherRentalId);

        // 440,000 total - 40,000 VAT - 0 deposit = 400,000 basis => 40 base points.
        Assert.Equal(40, settledAward.Points);
        Assert.Contains("1.00x Member tier", settledAward.Reason, StringComparison.Ordinal);

        Assert.Equal(44, readBackAward.Points);
        Assert.Contains("1.10x Silver tier", readBackAward.Reason, StringComparison.Ordinal);

        // Outside a settlement nothing changes: the same repository still sums committed rows.
        Assert.Equal(480 + 40 + 44, await loyaltyRepo.GetLifetimeEarnedAsync(rental.CustomerId, CancellationToken.None));
    }

    /// <summary>
    /// The settlement path must hold exactly ONE pooled connection. Measured from the server's side
    /// (<c>pg_stat_activity</c>) from inside the settlement transaction, immediately before and immediately after
    /// the loyalty read.
    ///
    /// Deliberately NOT filtered on <c>application_name</c>: a connection pooler such as Neon's does not carry the
    /// client's application name through to the backend, so that filter silently sees zero rows there and the
    /// measurement becomes blind rather than failing. Instead this counts BUSY backends on this test database and
    /// excludes the observing connection, which is portable across a direct connection and a pooler: idle pooled
    /// connections from the fixtures are excluded by <c>state &lt;&gt; 'idle'</c>, and an open settlement
    /// transaction always occupies exactly one backend.
    /// </summary>
    [Fact]
    public async Task The_settlement_awards_loyalty_on_its_own_connection_and_opens_no_second_one()
    {
        var returnedAt = Phase0Harness.ScheduledEnd;
        var rental = await Phase0Harness.ReadyDirectRentalAsync(db.DataSource, returnedAt, depositPrice: 0m);

        await using var settlementDataSource = CreateSettlementDataSource(ProbeApplicationName, maxPoolSize: 5);

        var clock = new SettableTimeProvider(returnedAt);
        var port = new ObservingLoyaltyAward(
            new LoyaltyAwardAdapter(
                new NpgsqlLoyaltyRepository(settlementDataSource, clock),
                NullLogger<LoyaltyAwardAdapter>.Instance,
                new LoyaltyOptions()),
            cancellationToken => ProbeBackendCountAsync(cancellationToken));

        // Sanity: the settlement data source has not opened anything yet.
        Assert.Equal(0, await ProbeBackendCountAsync(CancellationToken.None));

        var service = Phase0Harness.Service(settlementDataSource, clock, loyaltyAward: port);
        await service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None);

        Assert.Equal(1, port.Calls);
        Assert.Equal(1, port.BackendsBeforeRead);
        Assert.Equal(1, port.BackendsAfterRead);
    }

    /// <summary>
    /// The pool-pressure consequence stated as a test: a pool of ONE connection must be enough for two
    /// simultaneous settlements of different rentals. If the loyalty read still took a second connection, the
    /// second settlement would wait for a connection that only it could release.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_settlements_of_different_rentals_fit_in_a_pool_of_one_connection()
    {
        var returnedAt = Phase0Harness.ScheduledEnd;
        var first = await Phase0Harness.ReadyDirectRentalAsync(db.DataSource, returnedAt, depositPrice: 0m);
        var second = await Phase0Harness.ReadyDirectRentalAsync(db.DataSource, returnedAt, depositPrice: 0m);

        await using var settlementDataSource = CreateSettlementDataSource(ProbeApplicationName + "-single", maxPoolSize: 1);

        var clock = new SettableTimeProvider(returnedAt);
        var repo = new NpgsqlLoyaltyRepository(settlementDataSource, clock);
        var loyalty = new LoyaltyAwardAdapter(repo, NullLogger<LoyaltyAwardAdapter>.Instance, new LoyaltyOptions());

        var firstService = Phase0Harness.Service(settlementDataSource, clock, loyaltyAward: loyalty);
        var secondService = Phase0Harness.Service(settlementDataSource, clock, loyaltyAward: loyalty);

        await Task.WhenAll(
                firstService.CompleteAsync(first.RentalId, Phase0Harness.Staff, CancellationToken.None),
                secondService.CompleteAsync(second.RentalId, Phase0Harness.Staff, CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal("completed", await Phase0Harness.RentalStatusAsync(db.DataSource, first.RentalId));
        Assert.Equal("completed", await Phase0Harness.RentalStatusAsync(db.DataSource, second.RentalId));
        // Exactly one earn row per settled rental: both settlements really did run to the end.
        Assert.Equal(2, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM customer_loyalty_transactions WHERE rental_id IN ($1, $2) AND type = 'earn'",
            first.RentalId, second.RentalId));
    }

    /// <summary>
    /// A second data source over the same database, tagged with its own application name so
    /// <c>pg_stat_activity</c> can count exactly the connections a settlement opened. The password has to be
    /// re-applied: Npgsql drops it from <see cref="NpgsqlDataSource.ConnectionString"/> because Persist Security
    /// Info defaults to false. TEST_DATABASE_ADMIN is the same variable <see cref="PostgresFixture"/> uses.
    /// </summary>
    private NpgsqlDataSource CreateSettlementDataSource(string applicationName, int maxPoolSize)
    {
        var admin = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("TEST_DATABASE_ADMIN")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres");

        var probe = new NpgsqlConnectionStringBuilder(db.DataSource.ConnectionString)
        {
            Password = admin.Password,
            ApplicationName = applicationName,
            MaxPoolSize = maxPoolSize,
            PersistSecurityInfo = true
        };

        return NpgsqlDataSource.Create(probe.ConnectionString);
    }

    private async Task<int> ProbeBackendCountAsync(CancellationToken ct)
    {
        await using var command = db.DataSource.CreateCommand(
            """
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND state <> 'idle'
            """);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private async Task<(int Points, string Reason)> EarnRowAsync(int customerId, int rentalId)
    {
        await using var command = db.DataSource.CreateCommand("""
            SELECT points, reason FROM customer_loyalty_transactions
            WHERE customer_id = $1 AND rental_id = $2 AND type = 'earn'
            """);
        command.Parameters.AddWithValue(customerId);
        command.Parameters.AddWithValue(rentalId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(),
            $"setup: expected exactly one earn row for customer {customerId} and rental {rentalId}.");
        return (reader.GetInt32(0), reader.GetString(1));
    }
}

/// <summary>
/// Test-side <see cref="ILoyaltyAward"/>: awards the rental being settled, then awards a second rental of the
/// same customer. Both awards happen inside the one settlement transaction, so the second award's tier lookup is
/// exactly the "reads rows written earlier in this transaction" case.
/// </summary>
internal sealed class SecondAwardInSameSettlementPort(ILoyaltyAward inner, int secondRentalId) : ILoyaltyAward
{
    /// <summary>How many awards were actually attempted inside the single settlement transaction.</summary>
    public int Awards { get; private set; }

    public async Task AwardPointsAsync(int customerId, int rentalId, decimal basisAmount, CancellationToken ct)
    {
        Awards++;
        await inner.AwardPointsAsync(customerId, rentalId, basisAmount, ct);
        Awards++;
        await inner.AwardPointsAsync(customerId, secondRentalId, basisAmount, ct);
    }
}

/// <summary>
/// Test-side <see cref="ILoyaltyAward"/> that counts the server-side connections of the settlement data source
/// immediately before and after the real loyalty award, while the settlement transaction is still open.
/// </summary>
internal sealed class ObservingLoyaltyAward(ILoyaltyAward inner, Func<CancellationToken, Task<int>> probe) : ILoyaltyAward
{
    public int Calls { get; private set; }

    public int BackendsBeforeRead { get; private set; } = -1;

    public int BackendsAfterRead { get; private set; } = -1;

    public async Task AwardPointsAsync(int customerId, int rentalId, decimal basisAmount, CancellationToken ct)
    {
        Calls++;
        BackendsBeforeRead = await probe(ct);
        await inner.AwardPointsAsync(customerId, rentalId, basisAmount, ct);
        BackendsAfterRead = await probe(ct);
    }
}
