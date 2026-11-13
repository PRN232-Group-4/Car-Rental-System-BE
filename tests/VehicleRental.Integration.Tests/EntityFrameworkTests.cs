using System.Reflection;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

public sealed class EntityFrameworkModelTests
{
    [Fact]
    public void Short_lived_contexts_reuse_model_across_distinct_data_sources()
    {
        Microsoft.EntityFrameworkCore.Metadata.IModel? first = null;
        for (var index = 0; index < 25; index++)
        {
            using var source = NpgsqlDataSource.Create($"Host=localhost;Database=model_{index};Username=postgres");
            using var db = VehicleRentalDbContext.Create(source);
            first ??= db.Model;
            Assert.Same(first, db.Model);
        }
    }

    [Fact]
    public void Model_preserves_existing_tables_views_and_explicit_foreign_keys_without_navigations()
    {
        using var source = NpgsqlDataSource.Create("Host=localhost;Database=model_only;Username=postgres");
        using var db = VehicleRentalDbContext.Create(source);
        Assert.Equal(QueryTrackingBehavior.NoTracking, db.ChangeTracker.QueryTrackingBehavior);
        Assert.All(db.Model.GetEntityTypes(), entity => Assert.Empty(entity.GetNavigations()));
        Assert.Contains(db.Model.FindEntityType(typeof(RentalsRow))!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(CustomersRow) && fk.Properties.Single().Name == nameof(RentalsRow.CustomerId));
        Assert.All(db.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()),
            fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
        Assert.Equal("timestamp", db.Model.FindEntityType(typeof(RentalsRow))!
            .FindProperty(nameof(RentalsRow.CreatedAt))!.GetColumnType());
        Assert.Equal("customer_license_records", db.Model.FindEntityType(typeof(CustomerLicenseRecordsRow))!.GetViewName());
        Assert.Contains("WHERE", db.Set<UsersRow>().Where(u => u.Email == "test@example.local").ToQueryString());
        Assert.Contains("users", db.Set<UsersRow>().ToQueryString());
    }
}

public sealed class EntityFrameworkIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Every_mapped_table_and_view_can_be_queried_against_migrated_schema()
    {
        await using var db = VehicleRentalDbContext.Create(fixture.DataSource);
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var probe = typeof(EntityFrameworkIntegrationTests).GetMethod(nameof(Probe), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(entity.ClrType);
            await (Task)probe.Invoke(null, [db])!;
        }
    }

    private static async Task Probe<T>(VehicleRentalDbContext db) where T : class =>
        await db.Set<T>().Take(1).ToListAsync();

    [Fact]
    public async Task Account_query_observes_user_customer_and_role_memberships()
    {
        var repository = new NpgsqlUserRepository(fixture.DataSource);
        await repository.CreateCustomerAsync(new User("ef-account@test.local", "EF Account", "existing-hash"), default);
        var account = await new NpgsqlAccountRepository(fixture.DataSource).FindByEmailAsync("ef-account@test.local", default);
        Assert.NotNull(account);
        Assert.Equal("EF Account", account.FullName);
        Assert.Contains("Customer", account.Roles);
        Assert.Null(account.StaffId);
        Assert.True(await repository.EmailExistsAsync("ef-account@test.local", default));
    }

    [Fact]
    public async Task Context_shares_uncommitted_transaction_without_owning_connection()
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using var insert = new NpgsqlCommand("INSERT INTO roles(name) VALUES('Uncommitted EF role')", connection, transaction);
            await insert.ExecuteNonQueryAsync();
            await using (var db = VehicleRentalDbContext.Create(connection, transaction))
                Assert.True(await db.Set<RolesRow>().AnyAsync(r => r.Name == "Uncommitted EF role"));
            await transaction.RollbackAsync();
        }
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        await using var after = VehicleRentalDbContext.Create(connection);
        Assert.False(await after.Set<RolesRow>().AnyAsync(r => r.Name == "Uncommitted EF role"));
    }
}

