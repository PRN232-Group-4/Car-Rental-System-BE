using Car_Rental_System_BE.Application.Features.Maps;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.WebApi.Features.Maps;

[ApiController]
[Route("api/map")]
[Authorize]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status502BadGateway)]
[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
public sealed class MapController(IMapService mapService) : ControllerBase
{
    /// <summary>
    /// Geocoding xuôi: chuyển địa chỉ dạng chữ thành tọa độ địa lý.
    /// </summary>
    [OperationDescription("Mapbox provider. Missing configuration returns 503, upstream failures 502/503, no route 404. No simulated coordinates or ETA. Provider IDs are Mapbox IDs.")]
    [SuccessMessage("Address geocoded successfully.")]
    [HttpGet("geocode")]
    [ProducesResponseType<GeocodeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Geocode([FromQuery] string address, CancellationToken ct)
    {
        var result = await mapService.GeocodeAsync(address, ct);
        if (result is null)
            return NotFound(new { message = $"No coordinates found for address '{address}'." });
        return Ok(result);
    }

    /// <summary>
    /// Geocoding ngược: chuyển tọa độ địa lý thành địa chỉ dễ đọc.
    /// </summary>
    [OperationDescription("Mapbox provider. Missing configuration returns 503, upstream failures 502/503, no route 404. No simulated coordinates or ETA. Provider IDs are Mapbox IDs.")]
    [SuccessMessage("Coordinates reverse-geocoded successfully.")]
    [HttpGet("reverse-geocode")]
    [ProducesResponseType<GeocodeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReverseGeocode([FromQuery] double lat, [FromQuery] double lng, CancellationToken ct)
    {
        var result = await mapService.ReverseGeocodeAsync(lat, lng, ct);
        if (result is null)
            return NotFound(new { message = $"No address found for location ({lat}, {lng})." });
        return Ok(result);
    }

    /// <summary>
    /// Tìm địa chỉ Việt Nam bằng Mapbox Geocoding v6 (tối đa 10 kết quả từ nhà cung cấp; không bảo đảm có POI).
    /// </summary>
    [OperationDescription("Mapbox provider. Missing configuration returns 503, upstream failures 502/503, no route 404. No simulated coordinates or ETA. Provider IDs are Mapbox IDs.")]
    [OperationDescription(" Vietnam address search via Geocoding v6, not POI search. Provider returns at most 10 matches; items/page/pageSize/totalCount paginate that match set, not the global catalog. Defaults page=1/pageSize=20; page>=1, pageSize 1..100. lat/lng must be supplied together; radius requires both and filters by meters.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Places retrieved successfully.")]
    [HttpGet("places")]
    [ProducesResponseType<PlaceSearchPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Places(
        [FromQuery] string query,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double? radius,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var places = await mapService.SearchPlacesAsync(query, lat, lng, radius, page, pageSize, ct);
        return Ok(places);
    }

    /// <summary>
    /// Tính khoảng cách đường bộ và thời gian ước tính bằng Mapbox Matrix API.
    /// </summary>
    [OperationDescription("Mapbox provider. Missing configuration returns 503, upstream failures 502/503, no route 404. No simulated coordinates or ETA. Provider IDs are Mapbox IDs.")]
    [SuccessMessage("Distance and ETA calculated successfully.")]
    [HttpGet("distance-matrix")]
    [ProducesResponseType<DistanceMatrixResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DistanceMatrix(
        [FromQuery] double originLat,
        [FromQuery] double originLng,
        [FromQuery] double destLat,
        [FromQuery] double destLng,
        CancellationToken ct)
    {
        var result = await mapService.GetDistanceMatrixAsync(originLat, originLng, destLat, destLng, ct);
        return Ok(result);
    }

    /// <summary>
    /// Tính hình học tuyến đường lái xe và thời gian bằng Mapbox Directions API (polyline độ chính xác 5).
    /// </summary>
    [OperationDescription("Mapbox provider. Missing configuration returns 503, upstream failures 502/503, no route 404. No simulated coordinates or ETA. Provider IDs are Mapbox IDs.")]
    [SuccessMessage("Directions calculated successfully.")]
    [HttpGet("directions")]
    [ProducesResponseType<DirectionsResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Directions(
        [FromQuery] double originLat,
        [FromQuery] double originLng,
        [FromQuery] double destLat,
        [FromQuery] double destLng,
        CancellationToken ct)
    {
        var result = await mapService.GetDirectionsAsync(originLat, originLng, destLat, destLng, ct);
        return Ok(result);
    }
}
