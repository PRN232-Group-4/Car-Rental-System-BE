# Register the staff repository in the composition root

## Fixed
- Registered `IStaffRepository` in DI (`Program.cs`), so `StaffController` can be constructed.
- Without it the only composition root never resolved the staff/admin endpoints; every `/api/staff*` call failed with a DI resolution error at request time. The compiler and `dotnet build` could not catch this because the controller only depends on the interface.

## Notes
- Scoped, matching the other Npgsql repositories; it depends on the singleton `NpgsqlDataSource`.
- The `NpgsqlDataSource` registration is conditional on a configured connection string, so the same condition applies to staff endpoints as to the existing account/user endpoints.

## Verified
- `dotnet build Car-Rental-System-BE.sln` succeeds with 0 warnings and 0 errors.

## Known gaps
- No DI-composition or database-backed endpoint test exists yet, so this class of missing registration is still only caught by review.
