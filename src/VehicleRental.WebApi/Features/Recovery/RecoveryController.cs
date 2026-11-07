using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Recovery;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Recovery;

/// <summary>
/// Vehicle recovery over HTTP: the legacy replacement of a rental's vehicle. The action used to sit on
/// <c>DispatchController</c>; route, verb, policy, route constraints, status codes, request and response
/// shapes, success message and swagger description travelled with it unchanged.
/// </summary>
[ApiController]
[Route("api")]
public sealed class RecoveryController(IRecoveryService recovery) : ControllerBase
{
    /// <summary>Manager/Admin replaces with the same seat count, preserving prices; ongoing trips require a breakdown report and a new driver.</summary>
    [SuccessMessage("Replacement vehicle dispatched with original rental pricing preserved.")]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("rental/{id:int}/replacements")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    public Task<Rental> ReplaceVehicle(int id, VehicleReplacementRequest request, CancellationToken ct)
        => recovery.ReplaceVehicleAsync(id,request.VehicleId,request.DriverStaffId,request.ReportId,Email(),ct);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
