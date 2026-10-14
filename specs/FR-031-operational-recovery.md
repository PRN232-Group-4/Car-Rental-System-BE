# FR-031 — Driver shifts and incident recovery (O2/O4/O5/BE-24)

- Manager/Admin create, edit and cancel driver shifts. Assignments must fit an active shift and cannot overlap another rental. Driver reads only their shifts.
- Manager/Admin assign drivers, including reassignment during a rental. CustomerService no longer dispatches drivers.
- Staff/Driver report damage or breakdown against a rental; a Driver must be its current assigned driver. Reports retain authenticated author, affected vehicle, evidence and status. Manager/Admin assign a qualified Maintenance staff member; report and maintenance are linked transactionally.
- A breakdown marks the affected vehicle unavailable. Unsafe inspection also blocks the vehicle. Inspection never creates maintenance, charges or refund requests automatically. Staff report damage separately.
- Failed pre-inspection leaves the rental pending for a replacement. Inspection evidence is immutable per rental/vehicle/type; replacement vehicles need their own passed pre-inspection. Post-inspection applies to the current vehicle.
- Manager/Admin replace with a different available vehicle of the same vehicle type and exact seat count (car seat data required). Original price snapshots, invoice and deposit are preserved; no replacement surcharge.
- During renting, replacement requires an assigned breakdown report for the current vehicle, a different qualified driver on duty without conflicts, and a passed replacement-vehicle pre-inspection. Replacement, assignment history and report resolution are atomic.
- Vehicle licensePlate is nullable VARCHAR(255), exposed in create/update/detail/search. Migration tolerates a column already added manually.

Validation: PostgreSQL integration tests cover exact seats, preserved prices, unsafe inspection/report separation, incident ownership, shift coverage and overlap, reassignment and recovery transaction rollback; API authorization tests cover roles.
