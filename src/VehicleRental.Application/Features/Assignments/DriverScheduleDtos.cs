using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Assignments;

public sealed record DriverShiftRequest([param:Range(1,int.MaxValue)] int DriverStaffId, DateTime StartTime, DateTime EndTime, bool Cancelled=false);
public sealed record DriverScheduleEntry(int AssignmentId, int RentalId, int VehicleId, int DriverStaffId, string DriverName, DateTime StartTime, DateTime EndTime, string RentalStatus, string? VehicleName = null, string? LicensePlate = null, int BufferMinutes = 0);
