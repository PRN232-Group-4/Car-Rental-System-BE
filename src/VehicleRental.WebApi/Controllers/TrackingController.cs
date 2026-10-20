using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Map;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/tracking")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class TrackingController(ITrackingService trackingService) : ControllerBase
{
    /// <summary>
    /// GPS device telemetry ingestion API (supports offline batching).
    /// Authenticates via X-Device-Id and X-Device-Key headers.
    /// </summary>
    [HttpPost("ingest")]
    [AllowAnonymous]
    [ProducesResponseType<IngestPositionsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Ingest(
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        [FromHeader(Name = "X-Device-Key")] string? deviceKey,
        [FromBody] IngestPositionsRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(deviceKey))
            return Unauthorized(new { message = "X-Device-Id and X-Device-Key headers are required." });

        try
        {
            var result = await trackingService.IngestTelemetryAsync(deviceId, deviceKey, request, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves the latest known position of a vehicle.
    /// </summary>
    [Authorize]
    [HttpGet("vehicles/{vehicleId:int}/last-position")]
    [ProducesResponseType<VehicleLocationDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LastPosition(int vehicleId, CancellationToken ct)
    {
        var position = await trackingService.GetVehicleLastPositionAsync(vehicleId, ct);
        if (position is null)
            return NotFound(new { message = $"No location data found for vehicle {vehicleId}." });

        return Ok(position);
    }

    /// <summary>
    /// Retrieves paginated historical GPS positions for a vehicle within an optional time range.
    /// </summary>
    [Authorize]
    [HttpGet("vehicles/{vehicleId:int}/history")]
    [ProducesResponseType<LocationHistoryPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        int vehicleId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var history = await trackingService.GetLocationHistoryAsync(vehicleId, from, to, page, pageSize, ct);
        return Ok(history);
    }

    /// <summary>
    /// Searches for vehicles within a geographic radius, optionally computing Mapbox road distance and ETA.
    /// </summary>
    [Authorize]
    [HttpGet("vehicles/nearby")]
    [ProducesResponseType<NearbyVehiclePage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Nearby(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery] double? radiusMeters,
        [FromQuery] bool includeEta = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var nearby = await trackingService.GetNearbyVehiclesAsync(lat, lng, radiusMeters, includeEta, page, pageSize, ct);
        return Ok(nearby);
    }

    /// <summary>
    /// Reconstructs the traveled route, metrics, and GPS breadcrumbs for a rental.
    /// </summary>
    [Authorize]
    [HttpGet("rentals/{rentalId:int}/route")]
    [ProducesResponseType<RentalRouteSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Route(int rentalId, CancellationToken ct)
    {
        try
        {
            var summary = await trackingService.GetRentalRouteAsync(rentalId, ct);
            return Ok(summary);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Verifies if a vehicle is returned at the designated facility and generates an alert if deviated.
    /// </summary>
    [Authorize]
    [HttpPost("rentals/{rentalId:int}/verify-dropoff")]
    [ProducesResponseType<DropoffVerificationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> VerifyDropoff(
        int rentalId,
        [FromQuery] double? currentLat,
        [FromQuery] double? currentLng,
        CancellationToken ct)
    {
        try
        {
            var result = await trackingService.VerifyDropoffLocationAsync(rentalId, currentLat, currentLng, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Manually triggers retention purge of GPS telemetry older than the specified retention days.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpPost("retention/purge")]
    [ProducesResponseType<PurgeRetentionResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PurgeRetention([FromQuery] int? olderThanDays, CancellationToken ct)
    {
        var result = await trackingService.PurgeTelemetryAsync(olderThanDays, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves paginated tracking alerts (e.g., route deviation, wrong return point, geofence violations).
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpGet("alerts")]
    [ProducesResponseType<TrackingAlertPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Alerts(
        [FromQuery] int? rentalId,
        [FromQuery] int? vehicleId,
        [FromQuery] bool? isResolved,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var alerts = await trackingService.ListAlertsAsync(rentalId, vehicleId, isResolved, page, pageSize, ct);
        return Ok(alerts);
    }

    /// <summary>
    /// Marks a tracking alert as resolved by staff.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpPost("alerts/{alertId:long}/resolve")]
    [ProducesResponseType<TrackingAlertDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveAlert(long alertId, CancellationToken ct)
    {
        try
        {
            var staffName = User.Identity?.Name ?? "Staff";
            var alert = await trackingService.ResolveAlertAsync(alertId, staffName, ct);
            return Ok(alert);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
