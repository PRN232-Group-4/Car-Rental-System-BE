using Car_Rental_System_BE.Application.DTOs.Map;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/map")]
[Authorize]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class MapController(IMapService mapService) : ControllerBase
{
    /// <summary>
    /// Forward Geocoding: converts a text address to geographical coordinates.
    /// </summary>
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
    /// Reverse Geocoding: converts geographical coordinates into a human-readable address.
    /// </summary>
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
    /// Searches for places/addresses using Google Places API with optional geographical bias.
    /// </summary>
    [HttpGet("places")]
    [ProducesResponseType<IReadOnlyList<PlaceSearchResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Places(
        [FromQuery] string query,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double? radius,
        CancellationToken ct)
    {
        var places = await mapService.SearchPlacesAsync(query, lat, lng, radius, ct);
        return Ok(places);
    }

    /// <summary>
    /// Calculates driving distance and estimated time of arrival (ETA) between two points using Google Distance Matrix API.
    /// </summary>
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
    /// Computes full driving directions, turn-by-turn waypoints, and polyline using Google Directions API.
    /// </summary>
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
