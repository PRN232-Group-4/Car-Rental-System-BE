using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace VehicleRental.Tests;

public sealed class VehicleCrudTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlOperationsRepository repository = new(db.DataSource);
    private async Task<int> Category(string type, string fuel) =>
        (await repository.SaveVehicleTypeAsync(null, "Test", fuel, default, type)).Id;
    private static Vehicle Input(int type, VehicleDetail? detail = null) =>
        new(0, type, "", null, null, "available", "Test", "Model", null, 2025, 10, 100, null, null, null, detail);

    [Theory]
    [InlineData("car", "gasoline")]
    [InlineData("car", "electric")]
    [InlineData("motorbike", "gasoline")]
    [InlineData("motorbike", "electric")]
    public async Task Create_read_update_delete_uses_category(string type, string fuel)
    {
        var detail = type == "car" ? new VehicleDetail(SeatCount: 4) : new VehicleDetail(BikeType: "scooter", HasStorageBox: true);
        if (fuel == "electric") detail = detail with { ChargingTimeHours = "4", BatteryType = "Lithium" };
        detail = detail with { GalleryImages = new[] { "https://example.com/car.jpg" } };
        var saved = await repository.SaveVehicleAsync(null, Input(await Category(type, fuel), detail), default);
        Assert.Equal(detail.SeatCount, saved.Detail!.SeatCount);
        Assert.Equal(detail.BikeType, saved.Detail.BikeType);
        Assert.Equal(detail.ChargingTimeHours, saved.Detail.ChargingTimeHours);
        foreach (var table in new[] { "car_detail", "motorbike_detail", "electric_car_detail", "electric_motorbike_detail" })
        {
            var expected = table == type + "_detail" || (fuel == "electric" && table == "electric_" + type + "_detail");
            Assert.Equal(expected ? 1 : 0, await Seed.CountAsync(db.DataSource, $"SELECT count(*) FROM {table} WHERE vehicle_id=$1", saved.Id));
        }
        var updated = await repository.SaveVehicleAsync(saved.Id, saved with { Model = "Updated", Detail = null }, default);
        Assert.Equal("Updated", updated.Model);
        Assert.Equal(detail.ChargingTimeHours, updated.Detail!.ChargingTimeHours);
        await repository.DeleteVehicleAsync(saved.Id, default);
        Assert.Null(await repository.GetVehicleAsync(saved.Id, default));
        foreach (var table in new[] { "car_detail", "motorbike_detail", "electric_car_detail", "electric_motorbike_detail", "vehicle_imgs" })
            Assert.Equal(0, await Seed.CountAsync(db.DataSource, $"SELECT count(*) FROM {table} WHERE vehicle_id=$1", saved.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.DeleteVehicleAsync(saved.Id, default));
    }

    [Fact]
    public async Task Changing_category_removes_incompatible_details_and_preserves_gallery()
    {
        var saved = await repository.SaveVehicleAsync(null, Input(await Category("car", "electric"),
            new VehicleDetail(SeatCount: 4, BatteryCapacity: "50", GalleryImages: new[] { "https://example.com/1.jpg" })), default);
        var updated = await repository.SaveVehicleAsync(saved.Id, saved with {
            VehicleTypeId = await Category("motorbike", "gasoline"), Detail = new VehicleDetail(BikeType: "scooter") }, default);
        Assert.Null(updated.Detail!.SeatCount);
        Assert.Null(updated.Detail.BatteryCapacity);
        Assert.Equal("scooter", updated.Detail.BikeType);
        Assert.Single(updated.Detail.GalleryImages!);
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM electric_car_detail WHERE vehicle_id=$1", saved.Id));
    }

    [Fact]
    public async Task Invalid_details_are_rejected_and_failed_image_write_rolls_back_update()
    {
        var type = await Category("car", "gasoline");
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveVehicleAsync(null, Input(type, new VehicleDetail(BatteryType: "Lithium")), default));
        var saved = await repository.SaveVehicleAsync(null, Input(type, new VehicleDetail(SeatCount: 4)), default);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveVehicleAsync(saved.Id, saved with {
            Model = "Must rollback", Detail = new VehicleDetail(SeatCount: 7, GalleryImages: new[] { new string('x', 256) }) }, default));
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        var unchanged = await repository.GetVehicleAsync(saved.Id, default);
        Assert.Equal("Model", unchanged!.Model);
        Assert.Equal(4, unchanged.Detail!.SeatCount);
    }

    [Fact]
    public async Task Vehicle_with_history_cannot_be_deleted()
    {
        var saved = await repository.SaveVehicleAsync(null, Input(await Category("motorbike", "electric"), new VehicleDetail(BatteryType: "Lithium")), default);
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_maintenance(vehicle_id) VALUES($1) RETURNING id", saved.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteVehicleAsync(saved.Id, default));
        Assert.NotNull(await repository.GetVehicleAsync(saved.Id, default));
    }
}
