using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Map;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize(Policy = AuthorizationPolicies.Management)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class DevicesController(ITrackingService trackingService) : ControllerBase
{
    /// <summary>
    /// Lists registered GPS tracking devices with pagination and optional filters.
    /// </summary>
    [SuccessMessage("Tracking devices retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<DevicePage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? vehicleId,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var devices = await trackingService.ListDevicesAsync(vehicleId, isActive, page, pageSize, ct);
        return Ok(devices);
    }

    /// <summary>
    /// Registers a new GPS tracking device and returns its generated secret API key.
    /// </summary>
    [SuccessMessage("Tracking device registered successfully.")]
    [HttpPost]
    [ProducesResponseType<RegisterDeviceResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request, CancellationToken ct)
    {
        var result = await trackingService.RegisterDeviceAsync(request, ct);
        return Created($"/api/device/{result.Id}", result);
    }

    /// <summary>
    /// Retrieves details of a specific tracking device.
    /// </summary>
    [SuccessMessage("Tracking device retrieved successfully.")]
    [HttpGet("~/api/device/{id}")]
    [ProducesResponseType<TrackingDeviceDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var device = await trackingService.GetDeviceByIdAsync(id, ct);
        if (device is null)
            return NotFound(new { message = $"Device '{id}' was not found." });
        return Ok(device);
    }

    /// <summary>
    /// Updates status or assigned vehicle for a tracking device.
    /// </summary>
    [SuccessMessage("Tracking device updated successfully.")]
    [HttpPut("~/api/device/{id}")]
    [ProducesResponseType<TrackingDeviceDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateDeviceRequest request, CancellationToken ct)
    {
        try
        {
            var device = await trackingService.UpdateDeviceAsync(id, request, ct);
            return Ok(device);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
