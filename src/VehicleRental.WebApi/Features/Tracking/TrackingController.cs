using Car_Rental_System_BE.Application.Features.Tracking;
using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Tracking;

[ApiController]
[Route("api/tracking")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class TrackingController(ITrackingService trackingService) : ControllerBase
{
    /// <summary>
    /// API tiếp nhận dữ liệu đo xa từ thiết bị GPS (hỗ trợ gửi theo lô khi mất mạng). Xác thực qua
    /// header X-Device-Id và X-Device-Key.
    /// </summary>
    [SuccessMessage("GPS telemetry coordinates ingested successfully.")]
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
    /// Lấy vị trí đã biết mới nhất của xe.
    /// </summary>
    [SuccessMessage("Vehicle last position retrieved successfully.")]
    [Authorize]
    [HttpGet("vehicle/{vehicleId:int}/last-position")]
    [ProducesResponseType<VehicleLocationDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LastPosition(int vehicleId, CancellationToken ct)
    {
        var position = await trackingService.GetVehicleLastPositionAsync(vehicleId, ct);
        if (position is null)
            return NotFound(new { message = $"No location data found for vehicle {vehicleId}." });

        return Ok(position);
    }

    /// <summary>
    /// Lấy phân trang lịch sử vị trí GPS của xe trong khoảng thời gian tùy chọn.
    /// </summary>
    [SuccessMessage("Vehicle location history retrieved successfully.")]
    [Authorize]
    [HttpGet("vehicle/{vehicleId:int}/history")]
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
    /// Tìm xe trong bán kính địa lý, có thể tính khoảng cách đường bộ và ETA bằng Mapbox.
    /// </summary>
    [SuccessMessage("Nearby vehicles retrieved successfully.")]
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
    /// Dựng lại tuyến đường đã đi, các chỉ số và vệt GPS của đơn thuê.
    /// </summary>
    [SuccessMessage("Rental route retrieved successfully.")]
    [Authorize]
    [HttpGet("rental/{rentalId:int}/route")]
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
    /// Xác minh xe có được trả đúng cơ sở chỉ định không và tạo cảnh báo nếu sai lệch.
    /// </summary>
    [SuccessMessage("Drop-off location verification completed successfully.")]
    [Authorize]
    [HttpPost("rental/{rentalId:int}/verify-dropoff")]
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
    /// Kích hoạt thủ công việc xóa dữ liệu đo xa GPS cũ hơn số ngày lưu giữ đã chỉ định.
    /// </summary>
    [SuccessMessage("Expired GPS telemetry purged successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpPost("retention/purge")]
    [ProducesResponseType<PurgeRetentionResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PurgeRetention([FromQuery] int? olderThanDays, CancellationToken ct)
    {
        var result = await trackingService.PurgeTelemetryAsync(olderThanDays, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lấy phân trang các cảnh báo định vị (ví dụ lệch tuyến, trả sai điểm, vi phạm vùng địa lý).
    /// </summary>
    [SuccessMessage("Tracking alerts retrieved successfully.")]
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
    /// Nhân viên đánh dấu một cảnh báo định vị là đã xử lý.
    /// </summary>
    [SuccessMessage("Tracking alert resolved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpPost("alert/{alertId:long}/resolve")]
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
