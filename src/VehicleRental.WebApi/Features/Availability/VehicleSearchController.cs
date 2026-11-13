using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.WebApi.Features.Availability;

[ApiController]
[Route("api")]
public sealed class VehicleSearchController(VehicleSearchService service) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("public-search")]
    [OperationDescription("Public unrented-vehicle search: only status available, at least one non-whitespace allowedLicenseTypes entry, and no rental currently renting/post_inspection, including when searching future dates. Vehicles without license configuration are excluded before pagination and totalCount, including driver bookings; management listing, detail and rental history retain these vehicles. category=car|electric_car|motorbike|electric_motorbike uses vehicle type and fuel; q searches combined brand/model, year and branch; seatCount is a minimum. transmission=automatic includes CVT; manual/cvt and legacy display values are supported. fuelType accepts petrol/gasoline/electric and Vietnamese display values. sort=recommended|price_asc|price_desc|newest|name_asc has a stable id tie-breaker. Prices use rentalType=hourly|longterm. facilityId restricts vehicles to one branch; with hasDriver=true, an active qualified driver at that same branch must be free for the requested window including assignment buffer. Drivers are assigned later, not reserved by search. Pagination defaults to page=1/pageSize=20; page>=1 and pageSize 1..100, invalid values return 400. totalCount is after filtering and remains correct on empty pages.")]
    [SuccessMessage("Vehicles retrieved successfully.")]
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

    [SuccessMessage("Vehicle availability retrieved successfully.")]
    [AllowAnonymous]
    [HttpGet("vehicle/{id:int}/availability")]
    [ProducesResponseType<AvailabilityResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Availability(int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var result = await service.CheckAvailabilityAsync(id, from, to, ct);
        return Ok(result);
    }

    [SuccessMessage("Vehicle busy slots retrieved successfully.")]
    [AllowAnonymous]
    [HttpGet("vehicle/{id:int}/busy-slots")]
    [ProducesResponseType<IReadOnlyList<BusySlot>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BusySlots(int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var slots = await service.BusySlotsAsync(id, from, to, ct);
        return Ok(slots);
    }

    [SuccessMessage("Active rental policy retrieved successfully.")]
    [AllowAnonymous]
    [HttpGet("vehicle-type/{id:int}/active-policy")]
    [ProducesResponseType<ActiveRentalPolicy>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivePolicy(int id, CancellationToken ct)
    {
        var policy = await service.ActivePolicyAsync(id, ct);
        return Ok(policy);
    }

    [SuccessMessage("Affected rentals retrieved successfully.")]
    [Authorize(Roles = "Admin,Manager,Staff,CustomerService,Maintenance")]
    [HttpGet("vehicle/{id:int}/affected-rentals")]
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
    [Range(1, 100)] int PageSize = 20,
    [RegularExpression("^(car|electric_car|motorbike|electric_motorbike)$")] string? Category = null)
{
    public VehicleSearchQuery ToQuery() => new(
        From, To, RentalType, VehicleTypeId, FacilityId, Q, Brand, MinPrice, MaxPrice,
        SeatCount, Transmission, BikeType, MinRangeKm, YearFrom, FuelType, HasDriver, Sort, Page, PageSize, Category);
}
