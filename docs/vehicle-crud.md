# Vehicle CRUD

Endpoints (Management policy):
- `POST /api/vehicles`
- `PUT /api/vehicles/{id}`
- `DELETE /api/vehicles/{id}` (204; 404 if absent; 400 if rental/operational history exists)

The request continues to use `vehicleTypeId`. The server reads `vehicle_types.type`
and `vehicle_types.fuel_type`; display names and the presence of detail fields do
not determine the category. `GET /api/vehicle-types` now returns `type` as well.

| type | fuel_type | Detail tables |
| --- | --- | --- |
| car | gasoline | car_detail |
| car | electric | car_detail + electric_car_detail |
| motorbike | gasoline | motorbike_detail |
| motorbike | electric | motorbike_detail + electric_motorbike_detail |

Example electric motorbike request (replace vehicleTypeId with the matching ID):

```json
{
  "vehicleTypeId": 3,
  "status": "available",
  "brand": "VinFast",
  "model": "Evo",
  "hourlyPrice": 30000,
  "longtermPrice": 1500000,
  "detail": {
    "bikeType": "scooter",
    "hasStorageBox": true,
    "batteryCapacity": "3.5 kWh",
    "chargingType": "AC",
    "chargingTimeHours": "6",
    "maxRangeKm": 150,
    "batteryType": "LFP",
    "galleryImages": ["https://example.com/vehicle.jpg"]
  }
}
```

PUT uses the same body as POST for the main vehicle fields. If `detail` is omitted
or null, compatible existing details are retained. A provided detail replaces the
category-specific scalar fields (omitted fields become null). `galleryImages: null`
or omission retains the gallery; `[]` clears it. Changing `vehicleTypeId` removes
incompatible details even when detail is omitted. Main fields, detail cleanup,
detail writes and gallery writes are atomic. Deletion removes all four detail
rows and gallery rows atomically, after checking for related business records.

Car fields (`seatCount`, `transmission`, `trunkCapacity`) are rejected on motorbikes;
motorbike fields (`bikeType`, `hasStorageBox`) are rejected on cars. Battery and
charging fields are rejected on gasoline vehicles. Unknown or unconfigured types
and invalid facilities are rejected with 400.

Vehicle-type POST/PUT now require `name`, `fuelType` and `type`, for example:
`{"name":"Electric motorbike","fuelType":"electric","type":"motorbike"}`.
Category values are normalized. An in-use category cannot be reclassified through
these endpoints; create another category and update individual vehicles instead.

Apply `db/migrations/0014_vehicle_categories.sql` through the existing migration
runner before using the updated API. It adds `type` if absent and preserves manually
configured values. On older databases, explicitly configure each existing category
as `car`/`motorbike` with `gasoline`/`electric`; the migration does not guess from names.
Existing battery records previously written into the wrong table are not automatically
moved; inspect and correct those records before relying on the new category-based reads.

Integration tests: `VehicleCrudTests` uses a disposable PostgreSQL database via
`TEST_DATABASE_ADMIN` and covers all four categories, updates, cleanup, deletion,
rollback and history protection.
