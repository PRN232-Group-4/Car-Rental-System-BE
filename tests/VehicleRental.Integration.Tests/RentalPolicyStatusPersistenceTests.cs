using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalPolicyStatusPersistenceTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private NpgsqlFleetRepository Fleet => new(db.DataSource);

    [Fact]
    public async Task Status_only_update_preserves_terms_existing_rental_snapshots_and_other_versions()
    {
        var repository = new NpgsqlOperationsRepository(db.DataSource);
        var service = new OperationsService(repository);
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);
        var vehicle = (await Fleet.GetVehicleAsync(vehicleId, default))!;
        var previous = await repository.CreatePolicyAsync(new RentalPolicy(0, vehicle.VehicleTypeId, "Previous", 8m, 15m, 25m, false), default);
        var policy = await repository.CreatePolicyAsync(new RentalPolicy(0, vehicle.VehicleTypeId, "Current", 10m, 20m, 30m, true), default);
        var from = DateTime.UtcNow.AddDays(2);
        var rental = await repository.CreateRentalAsync(email, vehicleId, policy.Id, "hourly", from, from.AddHours(2), false, null, false, default);
        var before = await repository.GetRentalAsync(rental.Id, default);
        var invoiceAmount = await Seed.ScalarAsync(db.DataSource, "SELECT amount FROM invoices WHERE rental_id=$1", rental.Id);
        var count = await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_policies");
        var search = new NpgsqlVehicleSearchRepository(db.DataSource);

        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(policy with { IsActive = false, RentalCount = 1 }, await service.SetPolicyStatusAsync(policy.Id, false, default));
        previous = await service.SetPolicyStatusAsync(previous.Id, true, default);
        Assert.Equal(previous.Id, (await search.GetActivePolicyAsync(vehicle.VehicleTypeId, default))!.Id);
        Assert.Equal(before, await repository.GetRentalAsync(rental.Id, default));
        Assert.Equal(invoiceAmount, await Seed.ScalarAsync(db.DataSource, "SELECT amount FROM invoices WHERE rental_id=$1", rental.Id));
        Assert.Contains(previous, (await repository.ListPoliciesAsync(1, 100, default)).Items);
        Assert.Equal(count, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_policies"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetPolicyStatusAsync(policy.Id, true, default));
        await service.SetPolicyStatusAsync(previous.Id, false, default);
        Assert.Equal(policy with { RentalCount = 1 }, await service.SetPolicyStatusAsync(policy.Id, true, default));
        Assert.Equal(policy.Id, (await search.GetActivePolicyAsync(vehicle.VehicleTypeId, default))!.Id);
        Assert.Equal(before, await repository.GetRentalAsync(rental.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetPolicyStatusAsync(int.MaxValue, false, default));
    }
}
