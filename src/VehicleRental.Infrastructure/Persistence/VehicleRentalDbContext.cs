using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

/// <summary>
/// Read/write mappings for the existing SQL-managed schema. Contexts can enlist in an existing
/// Npgsql transaction so EF operations share its snapshot and PostgreSQL locks.
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
        // Rows are grouped in one file and explicitly mapped, without navigation properties; FKs are declared below.
        foreach (var type in typeof(VehicleRentalDbContext).Assembly.GetTypes()
                     .Where(t => t.Namespace == typeof(VehicleRentalDbContext).Namespace &&
                                 t.GetCustomAttribute<TableAttribute>() is not null))
            modelBuilder.Entity(type);
        modelBuilder.Entity<CustomerLicenseRecordsRow>().ToView("customer_license_records");
        modelBuilder.Entity<VVehicleMaintenanceBlocksRow>().ToView("v_vehicle_maintenance_blocks");
        modelBuilder.Entity<VLoyaltyReconciliationRow>().ToView("v_loyalty_reconciliation");
        ConfigureWriteBehaviour(modelBuilder);
        ConfigureForeignKeys(modelBuilder);
    }

    // Timestamp columns whose SQL default is evaluated by PostgreSQL. EF omits them from INSERT while the CLR
    // value is default(DateTime), so the database clock is used exactly as when the column was left out.
    // Constant defaults (status strings, TRUE flags, buffer minutes, empty arrays) are CLR initializers on the rows.
    private static readonly Dictionary<string, string> DatabaseTimestampDefaults = new()
    {
        ["created_at"] = "now()", ["status_changed_at"] = "now()", ["issued_at"] = "now()",
        ["replaced_at"] = "now()", ["updated_at"] = "now()",
    };

    private static readonly Dictionary<(string Table, string Column), string> UtcTimestampDefaults = new()
    {
        [("inspection_assignments", "assigned_at")] = "(now() AT TIME ZONE 'utc')",
        [("rental_contract_documents", "uploaded_at")] = "(now() AT TIME ZONE 'utc')",
        [("rental_orders", "created_at")] = "(now() AT TIME ZONE 'utc')",
        [("report_history", "created_at")] = "(now() AT TIME ZONE 'utc')",
        [("vehicle_assignments", "assigned_at")] = "now()",
    };

    private static void ConfigureWriteBehaviour(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes().Where(e => e.GetTableName() is not null))
        {
            var table = entity.GetTableName()!;
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(DateTime)))
            {
                var column = property.GetColumnName();
                if (UtcTimestampDefaults.TryGetValue((table, column), out var sql) || DatabaseTimestampDefaults.TryGetValue(column, out sql))
                    property.SetDefaultValueSql(sql);
            }

            // Natural/shared primary keys (vehicle_id, rental_id, device id) are supplied by the caller, never identity.
            var key = entity.FindPrimaryKey();
            if (key is { Properties.Count: 1 } && key.Properties[0].GetColumnName() != "id")
                key.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }

        // STORED generated tsrange columns: never written by EF.
        foreach (var period in new[] { b.Entity<VehicleAssignmentsRow>().Property(x => x.Period), b.Entity<VehicleBookingsRow>().Property(x => x.Period) })
        {
            period.ValueGeneratedOnAddOrUpdate();
            period.Metadata.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            period.Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        }
    }

    /// <summary>
    /// Mirrors the FK constraints from db/migrations (0013, 0017, 0034). No navigation properties:
    /// EF uses these only to order SaveChanges and to document relations; the database performs ON DELETE.
    /// </summary>
    private static void ConfigureForeignKeys(ModelBuilder b)
    {
        Fk<SubscriptionHourLedgerRow, RentalsRow>(b, nameof(SubscriptionHourLedgerRow.RentalId));
        Fk<SubscriptionHourLedgerRow, CustomerSubscriptionsRow>(b, nameof(SubscriptionHourLedgerRow.CustomerSubscriptionId));
        Fk<UserRolesRow, UsersRow>(b, nameof(UserRolesRow.UserId));
        Fk<UserRolesRow, RolesRow>(b, nameof(UserRolesRow.RoleId));
        Fk<StaffsRow, UsersRow>(b, nameof(StaffsRow.UserId));
        Fk<StaffsRow, DepartmentsRow>(b, nameof(StaffsRow.DepartmentId));
        Fk<StaffsRow, FacilitiesRow>(b, nameof(StaffsRow.FacilityId));
        Fk<CustomersRow, UsersRow>(b, nameof(CustomersRow.UserId));
        Fk<VehiclesRow, FacilitiesRow>(b, nameof(VehiclesRow.FacilityId));
        Fk<VehiclesRow, VehicleTypesRow>(b, nameof(VehiclesRow.VehicleTypeId));
        Fk<VehiclesRow, StaffsRow>(b, nameof(VehiclesRow.CreatedBy));
        Fk<VehicleImgsRow, VehiclesRow>(b, nameof(VehicleImgsRow.VehicleId));
        Fk<CarDetailRow, VehiclesRow>(b, nameof(CarDetailRow.VehicleId));
        Fk<MotorbikeDetailRow, VehiclesRow>(b, nameof(MotorbikeDetailRow.VehicleId));
        Fk<ElectricCarDetailRow, VehiclesRow>(b, nameof(ElectricCarDetailRow.VehicleId));
        Fk<ElectricMotorbikeDetailRow, VehiclesRow>(b, nameof(ElectricMotorbikeDetailRow.VehicleId));
        Fk<RentalPoliciesRow, VehicleTypesRow>(b, nameof(RentalPoliciesRow.VehicleTypeId));
        Fk<RentalPoliciesRow, StaffsRow>(b, nameof(RentalPoliciesRow.ManagedBy));
        Fk<CustomerSubscriptionsRow, CustomersRow>(b, nameof(CustomerSubscriptionsRow.CustomerId));
        Fk<CustomerSubscriptionsRow, SubscriptionPlansRow>(b, nameof(CustomerSubscriptionsRow.PlanId));
        Fk<RentalsRow, CustomersRow>(b, nameof(RentalsRow.CustomerId));
        Fk<RentalsRow, VehiclesRow>(b, nameof(RentalsRow.VehicleId));
        Fk<RentalsRow, RentalPoliciesRow>(b, nameof(RentalsRow.PolicyId));
        Fk<RentalsRow, CustomerSubscriptionsRow>(b, nameof(RentalsRow.CustomerSubscriptionId));
        Fk<HourRentalDetailRow, RentalsRow>(b, nameof(HourRentalDetailRow.RentalId));
        Fk<HourRentalDetailRow, StaffsRow>(b, nameof(HourRentalDetailRow.StaffPickupId));
        Fk<LongtermRentalContractRow, RentalsRow>(b, nameof(LongtermRentalContractRow.RentalId));
        Fk<LongtermRentalContractRow, StaffsRow>(b, nameof(LongtermRentalContractRow.StaffPickUpId));
        Fk<VehicleAssignmentsRow, RentalsRow>(b, nameof(VehicleAssignmentsRow.RentalId));
        Fk<VehicleAssignmentsRow, VehiclesRow>(b, nameof(VehicleAssignmentsRow.VehicleId));
        Fk<VehicleAssignmentsRow, StaffsRow>(b, nameof(VehicleAssignmentsRow.DriverStaffId));
        Fk<VehicleAssignmentsRow, VehicleAssignmentsRow>(b, nameof(VehicleAssignmentsRow.SupersedesId));
        Fk<InspectionsRow, RentalsRow>(b, nameof(InspectionsRow.RentalId));
        Fk<InspectionsRow, VehiclesRow>(b, nameof(InspectionsRow.VehicleId));
        Fk<InspectionsRow, StaffsRow>(b, nameof(InspectionsRow.StaffId));
        Fk<InvoicesRow, RentalsRow>(b, nameof(InvoicesRow.RentalId));
        Fk<PaymentsRow, RentalsRow>(b, nameof(PaymentsRow.RentalId));
        Fk<PaymentsRow, InvoicesRow>(b, nameof(PaymentsRow.InvoiceId));
        Fk<PaymentsRow, SubscriptionPlansRow>(b, nameof(PaymentsRow.PlanId));
        Fk<PaymentsRow, CustomersRow>(b, nameof(PaymentsRow.CustomerId));
        Fk<VehicleMaintenanceRow, VehiclesRow>(b, nameof(VehicleMaintenanceRow.VehicleId));
        Fk<VehicleMaintenanceRow, FacilitiesRow>(b, nameof(VehicleMaintenanceRow.FacilityId));
        Fk<VehicleMaintenanceRow, StaffsRow>(b, nameof(VehicleMaintenanceRow.AssignedStaffId));
        Fk<VehicleMaintenanceRow, RentalsRow>(b, nameof(VehicleMaintenanceRow.RentalId));
        Fk<VehicleMaintenanceRow, InspectionsRow>(b, nameof(VehicleMaintenanceRow.InspectionId));
        Fk<ReportsRow, CustomersRow>(b, nameof(ReportsRow.CustomerId));
        Fk<ReportsRow, RentalsRow>(b, nameof(ReportsRow.RentalId));
        Fk<ReportsRow, VehicleMaintenanceRow>(b, nameof(ReportsRow.MaintenanceId));
        Fk<ReportsRow, StaffsRow>(b, nameof(ReportsRow.ReporterStaffId));
        Fk<ReportsRow, VehiclesRow>(b, nameof(ReportsRow.VehicleId));
        Fk<VehicleReplacementsRow, RentalsRow>(b, nameof(VehicleReplacementsRow.RentalId));
        Fk<VehicleReplacementsRow, ReportsRow>(b, nameof(VehicleReplacementsRow.ReportId));
        Fk<VehicleReplacementsRow, VehiclesRow>(b, nameof(VehicleReplacementsRow.OldVehicleId));
        Fk<VehicleReplacementsRow, VehiclesRow>(b, nameof(VehicleReplacementsRow.NewVehicleId));
        Fk<VehicleReplacementsRow, StaffsRow>(b, nameof(VehicleReplacementsRow.OldDriverStaffId));
        Fk<VehicleReplacementsRow, StaffsRow>(b, nameof(VehicleReplacementsRow.NewDriverStaffId));
        Fk<StaffRatingsRow, RentalsRow>(b, nameof(StaffRatingsRow.RentalId));
        Fk<StaffRatingsRow, StaffsRow>(b, nameof(StaffRatingsRow.StaffId));
        Fk<VehicleBookingsRow, RentalsRow>(b, nameof(VehicleBookingsRow.RentalId));
        Fk<VehicleBookingsRow, VehiclesRow>(b, nameof(VehicleBookingsRow.VehicleId));
        Fk<TrackingDevicesRow, VehiclesRow>(b, nameof(TrackingDevicesRow.VehicleId));
        Fk<VehiclePositionsRow, VehiclesRow>(b, nameof(VehiclePositionsRow.VehicleId));
        Fk<VehiclePositionsRow, TrackingDevicesRow>(b, nameof(VehiclePositionsRow.DeviceId));
        Fk<VehicleLastPositionsRow, VehiclesRow>(b, nameof(VehicleLastPositionsRow.VehicleId));
        Fk<VehicleLastPositionsRow, TrackingDevicesRow>(b, nameof(VehicleLastPositionsRow.DeviceId));
        Fk<GeofencesRow, FacilitiesRow>(b, nameof(GeofencesRow.FacilityId));
        Fk<GeofenceEventsRow, VehiclesRow>(b, nameof(GeofenceEventsRow.VehicleId));
        Fk<GeofenceEventsRow, RentalsRow>(b, nameof(GeofenceEventsRow.RentalId));
        Fk<GeofenceEventsRow, GeofencesRow>(b, nameof(GeofenceEventsRow.GeofenceId));
        Fk<TrackingAlertsRow, VehiclesRow>(b, nameof(TrackingAlertsRow.VehicleId));
        Fk<TrackingAlertsRow, RentalsRow>(b, nameof(TrackingAlertsRow.RentalId));
        Fk<RentalStatusHistoryRow, RentalsRow>(b, nameof(RentalStatusHistoryRow.RentalId));
        Fk<RentalStatusHistoryRow, UsersRow>(b, nameof(RentalStatusHistoryRow.ActorUserId));
        Fk<RentalSettlementsRow, RentalsRow>(b, nameof(RentalSettlementsRow.RentalId));
        Fk<RentalSettlementsRow, InvoicesRow>(b, nameof(RentalSettlementsRow.ShortfallInvoiceId));
        Fk<RentalSettlementsRow, PaymentsRow>(b, nameof(RentalSettlementsRow.RefundPaymentId));
        Fk<CustomerLoyaltyTransactionsRow, CustomersRow>(b, nameof(CustomerLoyaltyTransactionsRow.CustomerId));
        Fk<CustomerLoyaltyTransactionsRow, RentalsRow>(b, nameof(CustomerLoyaltyTransactionsRow.RentalId));
        Fk<RentalOrdersRow, RentalsRow>(b, nameof(RentalOrdersRow.RentalId));
        Fk<RentalOrdersRow, PaymentsRow>(b, nameof(RentalOrdersRow.PaymentId));
        Fk<UserAdministrationAuditRow, UsersRow>(b, nameof(UserAdministrationAuditRow.UserId));
        Fk<UserAdministrationAuditRow, UsersRow>(b, nameof(UserAdministrationAuditRow.ActorUserId));
        Fk<ReportHistoryRow, ReportsRow>(b, nameof(ReportHistoryRow.ReportId));
        Fk<RatingModerationHistoryRow, StaffRatingsRow>(b, nameof(RatingModerationHistoryRow.RatingId));
        Fk<InspectionAssignmentsRow, RentalsRow>(b, nameof(InspectionAssignmentsRow.RentalId));
        Fk<InspectionAssignmentsRow, VehiclesRow>(b, nameof(InspectionAssignmentsRow.VehicleId));
        Fk<InspectionAssignmentsRow, StaffsRow>(b, nameof(InspectionAssignmentsRow.TechnicianStaffId));
        Fk<CustomerLicensesRow, CustomersRow>(b, nameof(CustomerLicensesRow.CustomerId));
        Fk<RentalContractDocumentsRow, RentalsRow>(b, nameof(RentalContractDocumentsRow.RentalId));
    }

    // Shadow-free FK on an existing scalar column; NoAction keeps EF from cascading tracked rows itself.
    private static void Fk<TChild, TParent>(ModelBuilder b, string column) where TChild : class where TParent : class =>
        b.Entity<TChild>().HasOne<TParent>().WithMany().HasForeignKey(column).OnDelete(DeleteBehavior.NoAction);
}
