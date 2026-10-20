using Car_Rental_System_BE.Application.DTOs.Map;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.WebApi.Controllers;

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
    /// Searches Vietnamese addresses using Mapbox Geocoding v6 (up to 10 provider matches; no POI guarantee).
    /// </summary>
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
    /// Calculates road distance and estimated duration using Mapbox Matrix API.
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
    /// Computes driving route geometry and duration using Mapbox Directions API (precision-5 polyline).
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
