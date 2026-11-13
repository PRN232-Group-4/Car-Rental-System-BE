using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalLifecycleSchemaTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly DateTime T0 = new(2032, 6, 1, 8, 0, 0, DateTimeKind.Unspecified);

    private async Task<(int CustomerId, int VehicleId, int RentalId)> SeedBasicRentalAsync(string status = "pending")
    {
        var (cId, _) = await Seed.CustomerAsync(db.DataSource);
        var vId = await Seed.VehicleAsync(db.DataSource);

        var rentalId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rentals(customer_id, vehicle_id, rental_type, status, total_price, deposit_price)
            VALUES($1, $2, 'hourly', $3, 100, 20) RETURNING id
            """, cId, vId, status))!;

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO hour_rental_detail(rental_id, start_time, end_time, has_driver)
            VALUES($1, $2, $3, false)
            """, rentalId, T0, T0.AddHours(2));

        return (cId, vId, rentalId);
    }

    [Fact]
    public async Task Status_check_constraint_rejects_invalid_values()
    {
        var (cId, _) = await Seed.CustomerAsync(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO rentals(customer_id, vehicle_id, rental_type, status)
                VALUES($1, $2, 'hourly', 'invalid_status')
                """, cId, vehicleId));

        Assert.Equal("23514", ex.SqlState);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("pre_inspection")]
    [InlineData("renting")]
    [InlineData("post_inspection")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public async Task Status_check_constraint_accepts_valid_lifecycle_statuses(string status)
    {
        var (_, _, rentalId) = await SeedBasicRentalAsync(status);

        var currentStatus = (string)(await Seed.ScalarAsync(db.DataSource,
            "SELECT status FROM rentals WHERE id=$1", rentalId))!;

        Assert.Equal(status, currentStatus);
    }

    [Fact]
    public async Task Cancelled_by_check_constraint_rejects_invalid_values()
    {
        var (_, _, rentalId) = await SeedBasicRentalAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                UPDATE rentals SET cancelled_by = 'alien' WHERE id=$1
                """, rentalId));

        Assert.Equal("23514", ex.SqlState);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("staff")]
    [InlineData("system")]
    [InlineData(null)]
    public async Task Cancelled_by_check_constraint_accepts_valid_values(string? cancelledBy)
    {
        var (_, _, rentalId) = await SeedBasicRentalAsync();

        await Seed.ScalarAsync(db.DataSource, """
            UPDATE rentals SET cancelled_by = $2 WHERE id=$1
            """, rentalId, (object?)cancelledBy ?? DBNull.Value);

        var val = await Seed.ScalarAsync(db.DataSource,
            "SELECT cancelled_by FROM rentals WHERE id=$1", rentalId);

        Assert.Equal(cancelledBy, val is DBNull ? null : (string?)val);
    }

    [Fact]
    public async Task Status_history_is_immutable_rejects_update_and_delete()
    {
        var (_, _, rentalId) = await SeedBasicRentalAsync();

        var historyId = (long)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rental_status_history(rental_id, from_status, to_status, actor_role, reason)
            VALUES($1, 'pending', 'pre_inspection', 'staff', 'Manual confirmation')
            RETURNING id
            """, rentalId))!;

        var updateEx = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                UPDATE rental_status_history SET reason = 'Tampered' WHERE id = $1
                """, historyId));

        // Migration 0033 separates loyalty's error code from the status-history trigger.
        Assert.Equal("P0001", updateEx.SqlState);
        Assert.Contains("immutable", updateEx.MessageText);

        var deleteEx = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                DELETE FROM rental_status_history WHERE id = $1
                """, historyId));

        Assert.Equal("P0001", deleteEx.SqlState);
        Assert.Contains("immutable", deleteEx.MessageText);
        Assert.Equal("Manual confirmation", await Seed.ScalarAsync(db.DataSource,"SELECT reason FROM rental_status_history WHERE id=$1",historyId));
    }

    [Fact]
    public async Task Invoices_kind_check_constraint_rejects_invalid_values()
    {
        var (_, _, rentalId) = await SeedBasicRentalAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO invoices(rental_id, invoice_number, amount, kind)
                VALUES($1, 'INV-TEST-001', 50000, 'unsupported_kind')
                """, rentalId));

        Assert.Equal("23514", ex.SqlState);
    }

    [Fact]
    public async Task Invoices_supports_rental_and_overtime_for_same_rental()
    {
        var (_, _, rentalId) = await SeedBasicRentalAsync();

        var invRentalId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO invoices(rental_id, invoice_number, amount, kind)
            VALUES($1, $2, 100000, 'rental') RETURNING id
            """, rentalId, $"INV-{Guid.NewGuid():N}"))!;

        var invOvertimeId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO invoices(rental_id, invoice_number, amount, kind)
            VALUES($1, $2, 25000, 'overtime') RETURNING id
            """, rentalId, $"INV-{Guid.NewGuid():N}-OT"))!;

        Assert.True(invRentalId > 0);
        Assert.True(invOvertimeId > 0);
    }

    [Fact]
    public async Task Invoices_unique_kind_prevents_duplicate_rental_invoices()
    {
        var (_, _, rentalId) = await SeedBasicRentalAsync();

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO invoices(rental_id, invoice_number, amount, kind)
            VALUES($1, $2, 100000, 'rental')
            """, rentalId, $"INV-{Guid.NewGuid():N}");

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO invoices(rental_id, invoice_number, amount, kind)
                VALUES($1, $2, 120000, 'rental')
                """, rentalId, $"INV-{Guid.NewGuid():N}"));

        Assert.Equal("23505", ex.SqlState);
    }

    [Fact]
    public async Task Rental_settlements_stores_overtime_and_deposit_reconciliation()
    {
        var (_, _, rentalId) = await SeedBasicRentalAsync("completed");

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rental_settlements(
                rental_id, scheduled_end, returned_at,
                overtime_minutes, overtime_hours, overtime_amount, overtime_vat,
                deposit_held, deposit_deducted, damage_review_required
            )
            VALUES($1, $2, $3, 90, 2, 200000, 20000, 500000, 220000, false)
            """, rentalId, T0.AddHours(2), T0.AddHours(3).AddMinutes(30));

        var hours = (int)(await Seed.ScalarAsync(db.DataSource,
            "SELECT overtime_hours FROM rental_settlements WHERE rental_id=$1", rentalId))!;
        var deducted = (decimal)(await Seed.ScalarAsync(db.DataSource,
            "SELECT deposit_deducted FROM rental_settlements WHERE rental_id=$1", rentalId))!;

        Assert.Equal(2, hours);
        Assert.Equal(220000m, deducted);
    }
}
