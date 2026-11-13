using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Inspections;

/// <summary>
/// Inspection evidence over HTTP: the pre-rental and post-rental check of the rental's own vehicle and the
/// candidate check of a replacement vehicle before dispatch. The actions used to sit on
/// <c>OperationsController</c> (Pre, Post) and <c>DispatchController</c> (InspectReplacement); route, verb,
/// policy, route constraints, status codes, request and response shapes, success messages and swagger
/// descriptions travelled with them unchanged.
/// </summary>
/// <remarks>
/// A controller of its own rather than a second action set on <c>InspectionsController</c>: that type carries a
/// class-level role list for the inspection assignment features, and ASP.NET Core ANDs class-level with
/// action-level policy metadata, so the three actions below would silently lose the reach they had.
/// </remarks>
[ApiController]
[Route("api")]
public sealed class InspectionRecordsController(IInspectionRecordService inspections) : ControllerBase
{
    [SuccessMessage("Pre-rental inspection recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Inspection)] [HttpPost("rental/{id:int}/inspections/pre")] public Task<IActionResult> Pre(int id,InspectionRequest x,CancellationToken ct)=>Inspect(id,x,"pre_rental",ct);
    [SuccessMessage("Post-rental inspection recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Inspection)] [HttpPost("rental/{id:int}/inspections/post")] public Task<IActionResult> Post(int id,InspectionRequest x,CancellationToken ct)=>Inspect(id,x,"post_rental",ct);

    /// <summary>Inspection staff checks a candidate replacement before Manager dispatches it.</summary>
    [SuccessMessage("Replacement vehicle inspection recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Inspection)]
    [HttpPost("rental/{id:int}/replacement-inspection/{vehicleId:int}")]
    [ProducesResponseType<Inspection>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InspectReplacement(int id, int vehicleId, InspectionRequest request, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.StaffId(User);
        if (staff is null || request.StaffId is not null && staff!=request.StaffId) return Forbid();
        return Ok(await inspections.InspectReplacementAsync(id,vehicleId,staff.Value,request.IsOk,request.OdometerKm,request.FuelBatteryLevel,request.DamageNotes,request.ImageUrls,ct));
    }

    private async Task<IActionResult> Inspect(int id, InspectionRequest request, string type, CancellationToken ct)
    {
        var staffId = AuthorizationPolicies.StaffId(User);
        if (staffId is null || (request.StaffId is not null && request.StaffId != staffId)) return Forbid();
        return Ok(await inspections.AddInspectionAsync(id, staffId.Value, type, request.IsOk,
            request.OdometerKm, request.FuelBatteryLevel, request.DamageNotes, request.ImageUrls, ct));
    }
}
