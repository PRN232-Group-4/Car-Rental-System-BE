using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Handovers;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Repositories;
using VehicleRental.Domain.Lifecycle;
using Xunit;

namespace VehicleRental.Tests;

public sealed class HandoverIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly ActorContext Actor = new(1, "manager@test.local", "Manager");
    private NpgsqlRentalLifecycleRepository Repository => new(db.DataSource);
    private NpgsqlOperationsRepository Operations => new(db.DataSource);
    private HandoverService Service => new(new RentalQueryService(new NpgsqlRentalQueries(db.DataSource, TimeProvider.System)),
        new RentalLifecycleService(Repository, null!, null!, null!, null!, new Events(),
            new RentalOptions(), new RentalCancellationOptions(), TimeProvider.System));

    private async Task<int> InspectorAsync()
    {
        var user = await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1) RETURNING id", $"handover-{Guid.NewGuid():N}@test.local");
        return (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id) VALUES($1) RETURNING id", user))!;
    }

    private async Task<(int Rental, int Vehicle, int Inspector)> PrepareAsync(bool assign = true, bool paid = true)
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        if (paid) await Scenarios.CompleteInvoiceAsync(db.DataSource, scenario.InvoiceId, scenario.InvoiceAmount);
        await Seed.ScalarAsync(db.DataSource, "UPDATE customers SET license_status='approved',license_expiry_date=CURRENT_DATE+365 WHERE id=$1", scenario.CustomerId);
        await Repository.UpdateToPreInspectionAsync(scenario.RentalId, DateTime.UtcNow, Actor, default);
        var inspector = await InspectorAsync();
        var inspection = await Operations.AddInspectionAsync(scenario.RentalId, inspector, "pre_rental", true, 1000, 80, null, null, default);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(scenario.RentalId, default))!.Status);
        if (assign) await Operations.AssignAsync(scenario.RentalId, null, Actor.Email, default);
        return (scenario.RentalId, inspection.VehicleId!.Value, inspector);
    }

    [Fact]
    public async Task Confirm_inspect_assign_handover_return_and_post_inspect_work_through_real_repositories()
    {
        var ready = await PrepareAsync();
        Assert.Equal("pending", (await Service.GetAsync(ready.Rental, default)).Status);
        Assert.Equal("renting", (await Service.ConfirmAsync(ready.Rental, Actor, default)).RentalStatus);
        Assert.Equal(true, await Seed.ScalarAsync(db.DataSource, "SELECT is_renting FROM vehicles WHERE id=$1", ready.Vehicle));
        await Repository.UpdateToPostInspectionAsync(ready.Rental, DateTime.UtcNow, Actor, default);
        await Operations.AddInspectionAsync(ready.Rental, ready.Inspector, "post_rental", true, 1100, 50, null, null, default);
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
        await Operations.AddInspectionAsync(scenario.RentalId, inspector, "pre_rental", true, null, null, null, null, default);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(scenario.RentalId, default))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Operations.AddInspectionAsync(scenario.RentalId, inspector,
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
        await Operations.AddInspectionAsync(ready.Rental, ready.Inspector, "pre_rental", true, null, null, null, null, default);
        var missingAssignment = await Assert.ThrowsAsync<ConflictException>(() => Repository.UpdateToRentingAsync(ready.Rental, newVehicle, DateTime.UtcNow, Actor, default));
        Assert.Equal("ASSIGNMENT_REQUIRED", missingAssignment.Code);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(ready.Rental, default))!.Status);
        Assert.Equal(false, await Seed.ScalarAsync(db.DataSource, "SELECT is_renting FROM vehicles WHERE id=$1", newVehicle));
    }

    [Fact]
    public async Task Concurrent_confirmations_produce_one_start_and_one_conflict()
    {
        var ready = await PrepareAsync();
        async Task<string> Confirm()
        {
            try { await Service.ConfirmAsync(ready.Rental, Actor, default); return "success"; }
            catch (ConflictException ex) { return ex.Code; }
        }
        var results = await Task.WhenAll(Confirm(), Confirm());
        Assert.Single(results, x => x == "success");
        Assert.Single(results, x => x == "ALREADY_IN_STATE");
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_status_history WHERE rental_id=$1 AND to_status='renting'", ready.Rental));
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
        Assert.Equal("maintenance", (await Operations.GetVehicleAsync(ready.Vehicle, default))!.Status);
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
