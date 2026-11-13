namespace Car_Rental_System_BE.Application.Features.Tracking;

public static class TrackingValidators
{
    public static void ValidatePagination(int page, int pageSize)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be greater than or equal to 1.");
        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "PageSize must be between 1 and 100.");
    }

    public static void ValidateCoordinates(double lat, double lng)
    {
        if (double.IsNaN(lat) || lat < -90.0 || lat > 90.0)
            throw new ArgumentOutOfRangeException(nameof(lat), "Latitude must be between -90 and 90 degrees.");
        if (double.IsNaN(lng) || lng < -180.0 || lng > 180.0)
            throw new ArgumentOutOfRangeException(nameof(lng), "Longitude must be between -180 and 180 degrees.");
    }

    public static void ValidateRadius(double radiusMeters)
    {
        if (double.IsNaN(radiusMeters) || radiusMeters <= 0.0 || radiusMeters > 100_000.0)
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Radius must be greater than 0 and less than or equal to 100,000 meters.");
    }

    public static void ValidateIngestRequest(IngestPositionsRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request), "Ingestion request cannot be null.");
        if (request.Positions is null || request.Positions.Count == 0)
            throw new ArgumentException("Positions collection cannot be empty.", nameof(request));
        if (request.Positions.Count > 1000)
            throw new ArgumentException("Batch cannot contain more than 1000 positions.", nameof(request));

        foreach (var pos in request.Positions)
        {
            ValidateCoordinates(pos.Lat, pos.Lng);
            if (pos.RecordedAt == default)
                throw new ArgumentException("RecordedAt timestamp is required for each position.", nameof(request));
        }
    }

    public static void ValidateRegisterDevice(RegisterDeviceRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Trim().Length > 64)
            throw new ArgumentException("Device ID must be between 1 and 64 characters.", nameof(request));
        if (request.VehicleId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.VehicleId), "VehicleId must be positive.");
    }

    public static void ValidateGeofence(CreateGeofenceRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 255)
            throw new ArgumentException("Geofence name must be between 1 and 255 characters.", nameof(request));
        if (request.ZoneType is not ("allowed" or "prohibited" or "facility"))
            throw new ArgumentException("ZoneType must be 'allowed', 'prohibited', or 'facility'.", nameof(request));
        ValidateCoordinates(request.CenterLat, request.CenterLng);
        ValidateRadius(request.RadiusMeters);
    }
}
