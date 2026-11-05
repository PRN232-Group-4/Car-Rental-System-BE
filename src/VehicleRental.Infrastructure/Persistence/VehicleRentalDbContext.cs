using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

/// <summary>
/// Read models for the existing SQL-managed schema. Contexts can enlist in an existing
/// Npgsql transaction so EF queries observe the same snapshot and locks as legacy writes.
/// </summary>
public sealed class VehicleRentalDbContext(DbContextOptions<VehicleRentalDbContext> options) : DbContext(options)
{
    public static VehicleRentalDbContext Create(NpgsqlDataSource dataSource) => new(
        new DbContextOptionsBuilder<VehicleRentalDbContext>()
            .UseNpgsql(dataSource.CreateConnection(), contextOwnsConnection: true)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);

    public static VehicleRentalDbContext Create(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
    {
        var context = new VehicleRentalDbContext(new DbContextOptionsBuilder<VehicleRentalDbContext>()
            .UseNpgsql(connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
        if (transaction is not null) context.Database.UseTransaction(transaction);
        return context;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Rows live in one file per module and are explicitly mapped, with no navigation/FK conventions.
        foreach (var type in typeof(VehicleRentalDbContext).Assembly.GetTypes()
                     .Where(t => t.Namespace == typeof(VehicleRentalDbContext).Namespace &&
                                 t.GetCustomAttribute<TableAttribute>() is not null))
            modelBuilder.Entity(type);
        modelBuilder.Entity<CustomerLicenseRecordsRow>().ToView("customer_license_records");
        modelBuilder.Entity<VVehicleMaintenanceBlocksRow>().ToView("v_vehicle_maintenance_blocks");
        modelBuilder.Entity<VLoyaltyReconciliationRow>().ToView("v_loyalty_reconciliation");
    }
}
