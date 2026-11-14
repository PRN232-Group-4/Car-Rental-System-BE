using Car_Rental_System_BE.Application.Features.Tracking;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Tracking;

[ApiController]
[Route("api/geofences")]
[Authorize(Policy = AuthorizationPolicies.Management)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class GeofencesController(ITrackingService trackingService) : ControllerBase
{
    /// <summary>
    /// Liệt kê mọi vùng địa lý vận hành đã định nghĩa.
    /// </summary>
    [SuccessMessage("Geofences retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GeofenceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var geofences = await trackingService.ListGeofencesAsync(ct);
        return Ok(geofences);
    }

    /// <summary>
    /// Tạo vùng địa lý mới (vùng được phép hoạt động, vùng cấm hoặc ranh giới cơ sở).
    /// </summary>
    [SuccessMessage("Geofence created successfully.")]
    [HttpPost]
    [ProducesResponseType<GeofenceDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateGeofenceRequest request, CancellationToken ct)
    {
        var geofence = await trackingService.CreateGeofenceAsync(request, ct);
        return Created($"/api/geofence/{geofence.Id}", geofence);
    }

    /// <summary>
    /// Liệt kê phân trang các sự kiện vào và ra vùng địa lý.
    /// </summary>
    [SuccessMessage("Geofence events retrieved successfully.")]
    [HttpGet("events")]
    [ProducesResponseType<GeofenceEventPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Events(
        [FromQuery] int? vehicleId,
        [FromQuery] int? rentalId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var events = await trackingService.ListGeofenceEventsAsync(vehicleId, rentalId, page, pageSize, ct);
        return Ok(events);
    }
}
