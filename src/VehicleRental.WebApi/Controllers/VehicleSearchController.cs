using Car_Rental_System_BE.Application.Availability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api")]
public sealed class VehicleSearchController(VehicleSearchService service) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("public-search")]
    [HttpGet("vehicles/search")]
    [ProducesResponseType<VehicleSearchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromQuery] VehicleSearchRequest request, CancellationToken ct)
    {
        if (request.From is not null || request.To is not null)
            Response.Headers.CacheControl = "no-store";
        else
            Response.Headers.CacheControl = "public, max-age=30";

        var result = await service.SearchAsync(request.ToQuery(), ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("vehicles/{id:int}/availability")]
    [ProducesResponseType<AvailabilityResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Availability(int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var result = await service.CheckAvailabilityAsync(id, from, to, ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("vehicles/{id:int}/busy-slots")]
    [ProducesResponseType<IReadOnlyList<BusySlot>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BusySlots(int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var slots = await service.BusySlotsAsync(id, from, to, ct);
        return Ok(slots);
    }

    [AllowAnonymous]
    [HttpGet("vehicle-types/{id:int}/active-policy")]
    [ProducesResponseType<ActiveRentalPolicy>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivePolicy(int id, CancellationToken ct)
    {
        var policy = await service.ActivePolicyAsync(id, ct);
        return Ok(policy);
    }

    [Authorize(Roles = "Admin,Manager,Staff,CustomerService,Maintenance")]
    [HttpGet("vehicles/{id:int}/affected-rentals")]
    [ProducesResponseType<IReadOnlyList<AffectedRental>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AffectedRentals(int id, CancellationToken ct)
    {
        var rentals = await service.AffectedRentalsAsync(id, ct);
        return Ok(rentals);
    }
}

public sealed record VehicleSearchRequest(
    DateTime? From = null,
    DateTime? To = null,
    string RentalType = "hourly",
    int? VehicleTypeId = null,
    [Range(1, int.MaxValue)] int? FacilityId = null,
    string? Q = null,
    string? Brand = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    int? SeatCount = null,
    string? Transmission = null,
    string? BikeType = null,
    decimal? MinRangeKm = null,
    int? YearFrom = null,
    string? FuelType = null,
    bool? HasDriver = null,
    string Sort = "recommended",
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 20)
{
    public VehicleSearchQuery ToQuery() => new(
        From, To, RentalType, VehicleTypeId, FacilityId, Q, Brand, MinPrice, MaxPrice,
        SeatCount, Transmission, BikeType, MinRangeKm, YearFrom, FuelType, HasDriver, Sort, Page, PageSize);
}
