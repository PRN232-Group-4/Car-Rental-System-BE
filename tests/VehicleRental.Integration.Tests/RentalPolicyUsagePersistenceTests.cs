using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalPolicyUsagePersistenceTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private NpgsqlFleetRepository Fleet => new(db.DataSource);

    [Fact]
    public async Task Counts_follow_exact_policy_references_and_survive_status_updates()
    {
        var repository = new NpgsqlOperationsRepository(db.DataSource);
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);
        var typeId = (await Fleet.GetVehicleAsync(vehicleId, default))!.VehicleTypeId;
        var old = await repository.CreatePolicyAsync(new RentalPolicy(0, typeId, "Old", 10, 20, 30, false), default);
        var current = await repository.CreatePolicyAsync(new RentalPolicy(0, typeId, "Current", 10, 20, 30, true), default);
        foreach (var status in new[] { "pending", "completed", "cancelled" })
            await Seed.ScalarAsync(db.DataSource, "INSERT INTO rentals(customer_id,vehicle_id,policy_id,rental_type,status) VALUES($1,$2,$3,'hourly',$4)", customerId, vehicleId, old.Id, status);
        // The same vehicle type must not attribute a rental without a policy to either version.
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO rentals(customer_id,vehicle_id,rental_type,status) VALUES($1,$2,'hourly','pending')", customerId, vehicleId);
        var page = await repository.ListPoliciesAsync(1, 100, default);
        Assert.Equal(3, page.Items.Single(p => p.Id == old.Id).RentalCount);
        Assert.Equal(0, page.Items.Single(p => p.Id == current.Id).RentalCount);
        Assert.Equal(3, (await repository.SetPolicyStatusAsync(old.Id, false, default))!.RentalCount);
        var empty = await repository.ListPoliciesAsync(int.MaxValue, 100, default);
        Assert.Empty(empty.Items);
        Assert.Equal(page.TotalCount, empty.TotalCount);
    }

    [Fact]
    public async Task Missing_type_is_rejected_and_parallel_creates_cannot_both_succeed()
    {
        var repository = new NpgsqlOperationsRepository(db.DataSource);
        var service = new OperationsService(repository);
        await Assert.ThrowsAsync<ValidationException>(() => repository.CreatePolicyAsync(new RentalPolicy(0, int.MaxValue, "Missing", 10, 20, 30, true), default));
        var vehicleId = await Seed.VehicleAsync(db.DataSource);
        var typeId = (await Fleet.GetVehicleAsync(vehicleId, default))!.VehicleTypeId;
        var policy = new RentalPolicy(0, typeId, "Concurrent", 10, 20, 30, true);
        async Task<bool> Create()
        {
            try { await service.CreatePolicyAsync(policy, default); return true; }
            catch (ValidationException) { return false; }
        }
        var results = await Task.WhenAll(Create(), Create());
        Assert.Single(results, success => success);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_policies WHERE vehicle_type_id=$1 AND is_active=true", typeId));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreatePolicyAsync(policy, default));
    }

    [Fact]
    public async Task Parallel_activation_only_allows_one_inactive_version_to_become_active()
    {
        var repository = new NpgsqlOperationsRepository(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);
        var typeId = (await Fleet.GetVehicleAsync(vehicleId, default))!.VehicleTypeId;
        var first = await repository.CreatePolicyAsync(new RentalPolicy(0, typeId, "First", 10, 20, 30, false), default);
        var second = await repository.CreatePolicyAsync(first with { Id = 0, Name = "Second" }, default);
        async Task<bool> Activate(int id)
        {
            try { await repository.SetPolicyStatusAsync(id, true, default); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(Activate(first.Id), Activate(second.Id));
        Assert.Single(results, success => success);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_policies WHERE vehicle_type_id=$1 AND is_active=true", typeId));
    }
}
