using System.Security.Claims;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Rentals;

/// <summary>
/// Rental booking and rental reads over HTTP: the two booking verbs and the flat rental detail. The actions used
/// to sit on <c>OperationsController</c> (Hourly, Longterm, Rental); route, verb, policy, route constraints,
/// status codes, request and response shapes, success messages and swagger descriptions travelled with them
/// unchanged.
/// </summary>
[ApiController]
[Route("api")]
public sealed class RentalBookingsController(IRentalBookingService bookings) : ControllerBase
{
    [OperationDescription("Customer creates a rental preserving hasDriver for both hourly and longterm. The pickup time must be at least Rental:Expiry:MinimumLeadMinutes (default 60) ahead of the booking moment; earlier start times return 400 RENTAL_START_TIME_TOO_SOON and nothing is written. The created request stores expire_at = booking time + Rental:Expiry:ApprovalWindowMinutes (default 60) for FR-042 auto-expiry. Only available vehicles without a currently renting/post_inspection rental can be booked; schedule/maintenance checks run inside the transaction under a vehicle lock. Driver bookings automatically record a passing pre-rental confirmation with source driver_default and actor system, without assigning a driver. Self-drive requires an approved customer license matching vehicle allowedLicenseTypes and valid through rental end, plus an explicit technician check or Admin confirmation before handover.")]
    [SuccessMessage("Hourly rental created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpPost("rentals/hourly")] public async Task<IActionResult> Hourly(RentalRequest x,CancellationToken ct)
    {
        var rental = await bookings.CreateRentalAsync(Email(),x.VehicleId,x.PolicyId,"hourly",x.StartTime,x.EndTime,x.SubscriptionId is not null,x.SubscriptionId,x.HasDriver,ct,x.PaymentMethod);
        return Created($"/api/rental/{rental.Id}", rental);
    }
    [OperationDescription("Customer creates a rental preserving hasDriver for both hourly and longterm. The pickup time must be at least Rental:Expiry:MinimumLeadMinutes (default 60) ahead of the booking moment; earlier start times return 400 RENTAL_START_TIME_TOO_SOON and nothing is written. The created request stores expire_at = booking time + Rental:Expiry:ApprovalWindowMinutes (default 60) for FR-042 auto-expiry. Only available vehicles without a currently renting/post_inspection rental can be booked; schedule/maintenance checks run inside the transaction under a vehicle lock. Driver bookings automatically record a passing pre-rental confirmation with source driver_default and actor system, without assigning a driver. Self-drive requires an approved customer license matching vehicle allowedLicenseTypes and valid through rental end, plus an explicit technician check or Admin confirmation before handover.")]
    [SuccessMessage("Long-term rental created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpPost("rentals/longterm")] public async Task<IActionResult> Longterm(RentalRequest x,CancellationToken ct)
    {
        var rental = await bookings.CreateRentalAsync(Email(),x.VehicleId,x.PolicyId,"longterm",x.StartTime,x.EndTime,x.SubscriptionId is not null,x.SubscriptionId,x.HasDriver,ct,x.PaymentMethod);
        return Created($"/api/rental/{rental.Id}", rental);
    }
    [OperationDescription("Authorized rental detail in the flat Rental contract. hasPassingPreInspection and hasPostInspection are boolean readiness flags derived only from persisted inspections for this rental's current vehicle; a successful pre-rental check is mandatory before handover. These fields do not replace server-side lifecycle validation.")]
    [SuccessMessage("Rental retrieved successfully.")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [Authorize(Policy=AuthorizationPolicies.RentalRead)] [HttpGet("rental/{id:int}")] public async Task<IActionResult> Rental(int id,CancellationToken ct)=>await bookings.GetRentalForUserAsync(id,Email(),AuthorizationPolicies.CanReadAllRentals(User),User.IsInRole("Customer"),User.IsInRole("Driver"),ct) is { } x?Ok(x):Problem("Rental was not found.", statusCode: StatusCodes.Status404NotFound);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
