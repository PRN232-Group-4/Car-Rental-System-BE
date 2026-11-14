using System.Text;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Handovers;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Features.Inspections;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Domain.Features.Rentals;
using Xunit;

namespace VehicleRental.Tests;

public sealed class HandoverIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly ActorContext Actor = new(1, "manager@test.local", "Manager");
    private NpgsqlRentalLifecycleRepository Repository => new(db.DataSource);
    private NpgsqlRentalBookingRepository Operations => new(db.DataSource);
    private NpgsqlInspectionRecordRepository Inspections => new(db.DataSource);
    private NpgsqlFleetRepository Fleet => new(db.DataSource);
    private HandoverService Service => new(new RentalQueryService(new NpgsqlRentalQueries(db.DataSource, TimeProvider.System)),
        new RentalLifecycleService(Repository, new RentalPaymentGateAdapter(db.DataSource), null!, null!, null!, new Events(),
            new RentalOptions(), new RentalCancellationOptions(), TimeProvider.System));

    private async Task<int> InspectorAsync()
    {
        var user = await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1) RETURNING id", $"handover-{Guid.NewGuid():N}@test.local");
        return (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id) VALUES($1) RETURNING id", user))!;
    }

    private async Task<(int Rental, int Vehicle, int Inspector)> PrepareAsync(bool assign = true, bool paid = true, string rentalType = "hourly")
    {
        var (customer, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.AddHours(1);
        var rental = await Operations.CreateRentalAsync(email, vehicle, null, rentalType, start,
            rentalType == "hourly" ? start.AddHours(2) : start.AddDays(30), false, null, false, default);
        if (rentalType == "longterm")
        {
            // A contract URL alone is insufficient: handover requires private persisted upload evidence.
            var document = new RentalContractDocument(Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n"), "application/pdf", "signed-contract.pdf");
            await new NpgsqlRentalContractDocumentRepository(db.DataSource)
                .SaveAsync(rental.Id, document, DateTime.UtcNow.AddMinutes(-1), Actor, default);
        }
        var invoice = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT id FROM invoices WHERE rental_id=$1", rental.Id))!;
        var amount = (decimal)(await Seed.ScalarAsync(db.DataSource, "SELECT amount FROM invoices WHERE id=$1", invoice))!;
        if (paid) await Scenarios.CompleteInvoiceAsync(db.DataSource, invoice, amount);
        await Seed.ScalarAsync(db.DataSource, "UPDATE customers SET license_status='approved',license_expiry_date=CURRENT_DATE+365 WHERE id=$1", customer);
        await Repository.UpdateToPreInspectionAsync(rental.Id, DateTime.UtcNow, Actor, default);
        var inspector = await InspectorAsync();
        var inspection = await Inspections.AddInspectionAsync(rental.Id, inspector, "pre_rental", true, 1000, 80, null, null, default);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(rental.Id, default))!.Status);
        // Same row the deleted lax AssignAsync wrote: handover has to reuse an existing assignment.
        if (assign) await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_assignments(rental_id,vehicle_id,driver_staff_id,status,assigned_by) SELECT id,vehicle_id,NULL,'assigned',$2 FROM rentals WHERE id=$1", rental.Id, Actor.Email);
        return (rental.Id, inspection.VehicleId!.Value, inspector);
    }

    [Theory]
    [InlineData("hourly", false)]
    [InlineData("hourly", true)]
    [InlineData("longterm", false)]
    [InlineData("longterm", true)]
    public async Task Self_drive_handover_records_selected_vehicle_or_reuses_existing_assignment(string rentalType, bool assigned)
    {
        var ready = await PrepareAsync(assign: assigned, rentalType: rentalType);
        Assert.Equal("renting", (await Service.ConfirmAsync(ready.Rental, Actor, default)).RentalStatus);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, """
            SELECT COUNT(*) FROM vehicle_assignments WHERE rental_id=$1 AND vehicle_id=$2
                AND status='assigned' AND driver_staff_id IS NULL AND assigned_by=$3
            """, ready.Rental, ready.Vehicle, Actor.Email));
        if (!assigned)
            Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM vehicle_assignments WHERE rental_id=$1 AND starts_at<ends_at", ready.Rental));
        Assert.True(await Repository.HasValidAssignmentAsync(ready.Rental, false, default));
        var repeated = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(ready.Rental, Actor, default));
        Assert.Equal("ALREADY_IN_STATE", repeated.Code);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM vehicle_assignments WHERE rental_id=$1", ready.Rental));
    }

    [Theory]
    [InlineData("unpaid", "PAYMENT_REQUIRED")]
    [InlineData("license", "CUSTOMER_LICENSE_NOT_VALID")]
    [InlineData("expired_license", "CUSTOMER_LICENSE_NOT_VALID")]
    [InlineData("inspection", "PRE_INSPECTION_REQUIRED")]
    [InlineData("maintenance", "VEHICLE_UNAVAILABLE")]
    public async Task Self_drive_atomic_handover_rejects_missing_evidence_without_creating_assignment(string blocker, string code)
    {
        var ready = await PrepareAsync(assign: false, paid: blocker != "unpaid");
        if (blocker == "license")
            await Seed.ScalarAsync(db.DataSource, "UPDATE customers SET license_status='pending' WHERE id=(SELECT customer_id FROM rentals WHERE id=$1)", ready.Rental);
        if (blocker == "expired_license")
            await Seed.ScalarAsync(db.DataSource, "UPDATE customers SET license_expiry_date=CURRENT_DATE-1 WHERE id=(SELECT customer_id FROM rentals WHERE id=$1)", ready.Rental);
        if (blocker == "inspection")
            await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET vehicle_id=$2 WHERE id=$1", ready.Rental, await Seed.VehicleAsync(db.DataSource));
        if (blocker == "maintenance")
            await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET status='maintenance' WHERE id=$1", ready.Vehicle);
        var currentVehicle = (await Operations.GetRentalAsync(ready.Rental, default))!.VehicleId;
        // Call the transaction boundary directly so application prechecks cannot hide a race.
        var error = await Assert.ThrowsAsync<ConflictException>(() => Repository.UpdateToRentingAsync(ready.Rental, currentVehicle, DateTime.UtcNow, Actor, default));
        Assert.Equal(code, error.Code);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(ready.Rental, default))!.Status);
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM vehicle_assignments WHERE rental_id=$1", ready.Rental));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Driver_rental_still_requires_an_actual_driver_on_current_vehicle(bool vehicleOnlyAssignment)
    {
        var ready = await PrepareAsync(assign: vehicleOnlyAssignment);
        await Seed.ScalarAsync(db.DataSource, "UPDATE hour_rental_detail SET has_driver=true WHERE rental_id=$1", ready.Rental);
        var applicationError = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(ready.Rental, Actor, default));
        Assert.Equal("ASSIGNMENT_REQUIRED", applicationError.Code);
        var transactionError = await Assert.ThrowsAsync<ConflictException>(() => Repository.UpdateToRentingAsync(ready.Rental, ready.Vehicle, DateTime.UtcNow, Actor, default));
        Assert.Equal("ASSIGNMENT_REQUIRED", transactionError.Code);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(ready.Rental, default))!.Status);
    }

    [Fact]
    public async Task Self_drive_handover_does_not_reuse_an_assignment_containing_a_driver()
    {
        var ready = await PrepareAsync();
        // Inconsistent legacy data must be corrected explicitly; handover never erases it.
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicle_assignments SET driver_staff_id=$2 WHERE rental_id=$1", ready.Rental, ready.Inspector);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(ready.Rental, Actor, default));
        Assert.Equal("ASSIGNMENT_REQUIRED", error.Code);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM vehicle_assignments WHERE rental_id=$1 AND driver_staff_id=$2", ready.Rental, ready.Inspector));
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(ready.Rental, default))!.Status);
    }

    [Fact]
    public async Task Confirm_inspect_assign_handover_return_and_post_inspect_work_through_real_repositories()
    {
        var ready = await PrepareAsync();
        Assert.Equal("pending", (await Service.GetAsync(ready.Rental, default)).Status);
        Assert.Equal("renting", (await Service.ConfirmAsync(ready.Rental, Actor, default)).RentalStatus);
        Assert.Equal(true, await Seed.ScalarAsync(db.DataSource, "SELECT is_renting FROM vehicles WHERE id=$1", ready.Vehicle));
        await Repository.UpdateToPostInspectionAsync(ready.Rental, DateTime.UtcNow, Actor, default);
        await Inspections.AddInspectionAsync(ready.Rental, ready.Inspector, "post_rental", true, 1100, 50, null, null, default);
        Assert.Equal("post_inspection", (await Operations.GetRentalAsync(ready.Rental, default))!.Status);
        Assert.Equal("completed", (await Service.GetAsync(ready.Rental, default)).Status);
        var list = await Service.ListAsync(new("completed", VehicleId: ready.Vehicle), default);
        Assert.Contains(list.Items, x => x.Rental.Id == ready.Rental);
    }

    [Fact]
    public async Task Legacy_pending_inspection_still_advances_to_pre_inspection_and_evidence_is_immutable()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var inspector = await InspectorAsync();
        await Inspections.AddInspectionAsync(scenario.RentalId, inspector, "pre_rental", true, null, null, null, null, default);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(scenario.RentalId, default))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Inspections.AddInspectionAsync(scenario.RentalId, inspector,
            "pre_rental", true, null, null, null, null, default));
    }

    [Fact]
    public async Task Old_vehicle_evidence_and_assignment_cannot_start_current_vehicle_even_at_transaction_boundary()
    {
        var ready = await PrepareAsync();
        var newVehicle = await Seed.VehicleAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET vehicle_id=$1 WHERE id=$2", newVehicle, ready.Rental);
        Assert.False(await Repository.HasPassingPreInspectionAsync(ready.Rental, default));
        Assert.False(await Repository.HasValidAssignmentAsync(ready.Rental, false, default));
        var missingInspection = await Assert.ThrowsAsync<ConflictException>(() => Repository.UpdateToRentingAsync(ready.Rental, newVehicle, DateTime.UtcNow, Actor, default));
        Assert.Equal("PRE_INSPECTION_REQUIRED", missingInspection.Code);
        await Inspections.AddInspectionAsync(ready.Rental, ready.Inspector, "pre_rental", true, null, null, null, null, default);
        var missingAssignment = await Assert.ThrowsAsync<ConflictException>(() => Repository.UpdateToRentingAsync(ready.Rental, newVehicle, DateTime.UtcNow, Actor, default));
        Assert.Equal("ASSIGNMENT_REQUIRED", missingAssignment.Code);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(ready.Rental, default))!.Status);
        Assert.Equal(false, await Seed.ScalarAsync(db.DataSource, "SELECT is_renting FROM vehicles WHERE id=$1", newVehicle));
    }

    [Fact]
    public async Task Concurrent_confirmations_produce_one_start_and_one_conflict()
    {
        var ready = await PrepareAsync(assign: false);
        async Task<string> Confirm()
        {
            try { await Service.ConfirmAsync(ready.Rental, Actor, default); return "success"; }
            catch (ConflictException ex) { return ex.Code; }
        }
        var results = await Task.WhenAll(Confirm(), Confirm());
        Assert.Single(results, x => x == "success");
        Assert.Single(results, x => x == "ALREADY_IN_STATE");
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_status_history WHERE rental_id=$1 AND to_status='renting'", ready.Rental));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM vehicle_assignments WHERE rental_id=$1 AND status='assigned' AND driver_staff_id IS NULL", ready.Rental));
    }

    [Fact]
    public async Task Handover_rechecks_payment_even_after_legacy_inspection_advanced_the_rental()
    {
        var ready = await PrepareAsync(paid: false);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(ready.Rental, Actor, default));
        Assert.Equal("PAYMENT_REQUIRED", error.Code);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(ready.Rental, default))!.Status);
    }

    [Fact]
    public async Task Handover_cannot_overwrite_vehicle_maintenance_status()
    {
        var ready = await PrepareAsync();
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET status='maintenance' WHERE id=$1", ready.Vehicle);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(ready.Rental, Actor, default));
        Assert.Equal("VEHICLE_UNAVAILABLE", error.Code);
        Assert.Equal("maintenance", (await Fleet.GetVehicleAsync(ready.Vehicle, default))!.Status);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(ready.Rental, default))!.Status);
    }

    [Fact]
    public async Task Handover_cannot_ignore_an_unsafe_maintenance_record_even_when_vehicle_is_available()
    {
        var ready = await PrepareAsync();
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_maintenance(vehicle_id,status,is_safe) VALUES($1,'completed',false)", ready.Vehicle);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(ready.Rental, Actor, default));
        Assert.Equal("VEHICLE_UNAVAILABLE", error.Code);
    }

    private sealed class Events : IRentalEventSink
    {
        public Task PublishStatusChangedAsync(RentalStatusChangedEvent evt, CancellationToken ct) => Task.CompletedTask;
    }
}
