using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Recovery;

public sealed record VehicleReplacementRequest([param:Range(1,int.MaxValue)] int VehicleId,
    [param:Range(1,int.MaxValue)] int? DriverStaffId, [param:Range(1,int.MaxValue)] int? ReportId);
