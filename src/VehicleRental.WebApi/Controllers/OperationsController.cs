using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api")]
public sealed class OperationsController(IOperationsRepository store) : ControllerBase
{
    [Authorize] [HttpGet("customer/license")] public async Task<IActionResult> License(CancellationToken ct) => Ok(await store.GetLicenseAsync(Email(),ct));
    [Authorize] [HttpPut("customer/license")] public async Task<IActionResult> SaveLicense(LicenseRequest x,CancellationToken ct) { if(x.ExpiryDate < DateOnly.FromDateTime(DateTime.UtcNow)) return Problem("License must not be expired.", statusCode: StatusCodes.Status400BadRequest); return Ok(await store.SaveLicenseAsync(Email(),new(x.Number,x.Type,x.IssueDate,x.ExpiryDate,x.ImageUrl,"pending"),ct)); }
    [Authorize(Roles="Admin,Staff")] [HttpPut("customers/{customerId:int}/license-status")] public Task<CustomerLicense> ReviewLicense(int customerId,LicenseReviewRequest x,CancellationToken ct) => store.ReviewLicenseAsync(customerId,x.Status.ToLowerInvariant(),ct);

    [HttpGet("vehicle-types")] public Task<IReadOnlyList<VehicleType>> VehicleTypes(CancellationToken ct)=>store.ListVehicleTypesAsync(ct);
    [Authorize(Roles="Admin,Staff")] [HttpPost("vehicle-types")] public async Task<IActionResult> AddType(NameRequest x,CancellationToken ct){var t=await store.SaveVehicleTypeAsync(null,x.Name,x.FuelType,ct);return Created($"api/vehicle-types/{t.Id}",t);}
    [Authorize(Roles="Admin,Staff")] [HttpPut("vehicle-types/{id:int}")] public Task<VehicleType> EditType(int id,NameRequest x,CancellationToken ct)=>store.SaveVehicleTypeAsync(id,x.Name,x.FuelType,ct);
    [Authorize(Roles="Admin")] [HttpDelete("vehicle-types/{id:int}")] public async Task<IActionResult> DeleteType(int id,CancellationToken ct){await store.DeleteVehicleTypeAsync(id,ct);return NoContent();}
    [HttpGet("vehicles")] public Task<VehiclePage> Vehicles([FromQuery]int? vehicleTypeId,[FromQuery]int? facilityId,[FromQuery]string? status,[FromQuery]DateTime? from,[FromQuery]DateTime? to,[FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)=>store.SearchVehiclesAsync(vehicleTypeId,facilityId,status,from,to,page,pageSize,ct);
    [HttpGet("vehicles/{id:int}")] public async Task<IActionResult> Vehicle(int id,CancellationToken ct)=>await store.GetVehicleAsync(id,ct) is { } x?Ok(x):Problem("Vehicle was not found.", statusCode: StatusCodes.Status404NotFound);
    [Authorize(Roles="Admin,Staff")] [HttpPost("vehicles")] public async Task<IActionResult> AddVehicle(VehicleRequest x,CancellationToken ct){var v=await store.SaveVehicleAsync(null,x.ToModel(),ct);return Created($"api/vehicles/{v.Id}",v);}
    [Authorize(Roles="Admin,Staff")] [HttpPut("vehicles/{id:int}")] public Task<Vehicle> EditVehicle(int id,VehicleRequest x,CancellationToken ct)=>store.SaveVehicleAsync(id,x.ToModel(),ct);

    [Authorize(Roles="Admin")] [HttpGet("departments")] public Task<IReadOnlyList<OrganizationUnit>> Departments(CancellationToken ct)=>store.ListDepartmentsAsync(ct);
    [Authorize(Roles="Admin")] [HttpPost("departments")] public async Task<IActionResult> AddDepartment(NameRequest x,CancellationToken ct){var d=await store.SaveDepartmentAsync(null,x.Name,ct);return Created($"api/departments/{d.Id}",d);}
    [Authorize(Roles="Admin")] [HttpPut("departments/{id:int}")] public Task<OrganizationUnit> EditDepartment(int id,NameRequest x,CancellationToken ct)=>store.SaveDepartmentAsync(id,x.Name,ct);
    [Authorize(Roles="Admin")] [HttpDelete("departments/{id:int}")] public async Task<IActionResult> DeleteDepartment(int id,CancellationToken ct){await store.DeleteDepartmentAsync(id,ct);return NoContent();}
    [Authorize(Roles="Admin")] [HttpGet("facilities")] public Task<IReadOnlyList<OrganizationUnit>> Facilities(CancellationToken ct)=>store.ListFacilitiesAsync(ct);
    [Authorize(Roles="Admin")] [HttpPost("facilities")] public async Task<IActionResult> AddFacility(FacilityRequest x,CancellationToken ct){var f=await store.SaveFacilityAsync(null,x.Name,x.Address,ct);return Created($"api/facilities/{f.Id}",f);}
    [Authorize(Roles="Admin")] [HttpPut("facilities/{id:int}")] public Task<OrganizationUnit> EditFacility(int id,FacilityRequest x,CancellationToken ct)=>store.SaveFacilityAsync(id,x.Name,x.Address,ct);
    [Authorize(Roles="Admin")] [HttpDelete("facilities/{id:int}")] public async Task<IActionResult> DeleteFacility(int id,CancellationToken ct){await store.DeleteFacilityAsync(id,ct);return NoContent();}

    [Authorize(Roles="Admin,Staff")] [HttpGet("rental-policies")] public Task<IReadOnlyList<RentalPolicy>> Policies(CancellationToken ct)=>store.ListPoliciesAsync(ct);
    [Authorize(Roles="Admin,Staff")] [HttpPost("rental-policies")] public async Task<IActionResult> AddPolicy(PolicyRequest x,CancellationToken ct){var p=await store.SavePolicyAsync(null,x.ToModel(),ct);return Created($"api/rental-policies/{p.Id}",p);}
    [Authorize(Roles="Admin,Staff")] [HttpPut("rental-policies/{id:int}")] public Task<RentalPolicy> EditPolicy(int id,PolicyRequest x,CancellationToken ct)=>store.SavePolicyAsync(id,x.ToModel(),ct);

    [Authorize] [HttpPost("rentals/hourly")] public async Task<IActionResult> Hourly(RentalRequest x,CancellationToken ct)=>Created("api/rentals",await store.CreateRentalAsync(Email(),x.VehicleId,x.PolicyId,"hourly",x.StartTime,x.EndTime,x.SubscriptionId is not null,x.SubscriptionId,x.HasDriver,ct));
    [Authorize] [HttpPost("rentals/longterm")] public async Task<IActionResult> Longterm(RentalRequest x,CancellationToken ct)=>Created("api/rentals",await store.CreateRentalAsync(Email(),x.VehicleId,x.PolicyId,"longterm",x.StartTime,x.EndTime,x.SubscriptionId is not null,x.SubscriptionId,false,ct));
    [Authorize] [HttpGet("rentals/{id:int}")] public async Task<IActionResult> Rental(int id,CancellationToken ct)=>await store.GetRentalAsync(id,ct) is { } x?Ok(x):Problem("Rental was not found.", statusCode: StatusCodes.Status404NotFound);
    [Authorize(Roles="Admin,Staff")] [HttpPost("rentals/{id:int}/assignments")] public async Task<IActionResult> Assign(int id,AssignmentRequest x,CancellationToken ct){await store.AssignAsync(id,x.DriverStaffId,ct);return NoContent();}
    [Authorize(Roles="Admin,Staff")] [HttpPost("rentals/{id:int}/inspections/pre")] public Task<Inspection> Pre(int id,InspectionRequest x,CancellationToken ct)=>store.AddInspectionAsync(id,x.StaffId,"pre_rental",x.IsOk,x.OdometerKm,x.FuelBatteryLevel,x.DamageNotes,x.ImageUrls,ct);
    [Authorize(Roles="Admin,Staff")] [HttpPost("rentals/{id:int}/inspections/post")] public Task<Inspection> Post(int id,InspectionRequest x,CancellationToken ct)=>store.AddInspectionAsync(id,x.StaffId,"post_rental",x.IsOk,x.OdometerKm,x.FuelBatteryLevel,x.DamageNotes,x.ImageUrls,ct);
    [Authorize(Roles="Admin,Staff")] [HttpPost("rentals/{id:int}/status/{status}")] public Task<Rental> Status(int id,string status,CancellationToken ct)=>store.ChangeRentalStatusAsync(id,status,ct);

    [HttpGet("subscription-plans")] public Task<IReadOnlyList<SubscriptionPlan>> Plans([FromQuery]bool all,CancellationToken ct)=>store.ListPlansAsync(!all,ct);
    [HttpGet("subscription-plans/{id:int}")] public async Task<IActionResult> Plan(int id,CancellationToken ct)=>await store.GetPlanAsync(id,ct) is { } x?Ok(x):Problem("Subscription plan was not found.", statusCode: StatusCodes.Status404NotFound);
    [Authorize(Roles="Admin,Staff")] [HttpPost("subscription-plans")] public async Task<IActionResult> AddPlan(PlanRequest x,CancellationToken ct){var p=await store.SavePlanAsync(null,x.ToModel(),ct);return Created($"api/subscription-plans/{p.Id}",p);}
    [Authorize(Roles="Admin,Staff")] [HttpPut("subscription-plans/{id:int}")] public Task<SubscriptionPlan> EditPlan(int id,PlanRequest x,CancellationToken ct)=>store.SavePlanAsync(id,x.ToModel(),ct);
    [Authorize(Roles="Admin")] [HttpDelete("subscription-plans/{id:int}")] public async Task<IActionResult> DeletePlan(int id,CancellationToken ct){await store.DeletePlanAsync(id,ct);return NoContent();}
    [Authorize] [HttpPost("subscription-plans/{id:int}/purchase")] public async Task<IActionResult> Purchase(int id,CancellationToken ct)=>Created("api/customer-subscriptions",new { id=await store.BuyPlanAsync(Email(),id,ct) });
    [Authorize(Roles="Admin,Staff,Maintenance")] [HttpPost("maintenance")] public async Task<IActionResult> AddMaintenance(MaintenanceRequest x,CancellationToken ct){var m=await store.SaveMaintenanceAsync(null,x.ToModel(),ct);return Created($"api/maintenance/{m.Id}",m);}
    [Authorize(Roles="Admin,Staff,Maintenance")] [HttpPut("maintenance/{id:int}")] public Task<Maintenance> EditMaintenance(int id,MaintenanceRequest x,CancellationToken ct)=>store.SaveMaintenanceAsync(id,x.ToModel(),ct);
    [Authorize(Roles="Admin")] [HttpGet("dashboard")] public Task<object> Dashboard([FromQuery]DateTime? from,[FromQuery]DateTime? to,CancellationToken ct)=>store.DashboardAsync(from,to,ct);
    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}

public sealed record NameRequest([param:Required,StringLength(255,MinimumLength=2)]string Name,[param:StringLength(255)]string? FuelType=null);
public sealed record FacilityRequest([param:Required,StringLength(255,MinimumLength=2)]string Name,[param:StringLength(255)]string? Address);
public sealed record LicenseRequest([param:Required,StringLength(255)]string Number,[param:Required,StringLength(255)]string Type,DateOnly? IssueDate,DateOnly ExpiryDate,[param:Url] string? ImageUrl);
public sealed record LicenseReviewRequest([param:RegularExpression("^(approved|rejected)$",ErrorMessage="Status must be approved or rejected.")]string Status);
public sealed record VehicleRequest([param:Range(1,int.MaxValue)]int VehicleTypeId,int? FacilityId,[param:Required] string Status,string? Brand,string? Model,string? Color,int? YearOfManufacture,[param:Range(0,double.MaxValue)]decimal? HourlyPrice,[param:Range(0,double.MaxValue)]decimal? LongtermPrice,string? Description,[param:Url]string? MainImageUrl,string? Location){public Vehicle ToModel()=>new(0,VehicleTypeId,"",FacilityId,null,Status.ToLowerInvariant(),Brand,Model,Color,YearOfManufacture,HourlyPrice,LongtermPrice,Description,MainImageUrl,Location);}
public sealed record PolicyRequest(int? VehicleTypeId,[param:Required] string Name,[param:Range(0,100)]decimal VatRate,[param:Range(0,100)]decimal OvertimeFeeRate,[param:Range(0,100)]decimal DepositPercentage,bool IsActive=true){public RentalPolicy ToModel()=>new(0,VehicleTypeId,Name,VatRate,OvertimeFeeRate,DepositPercentage,IsActive);}
public sealed record RentalRequest([param:Range(1,int.MaxValue)]int VehicleId,int? PolicyId,DateTime StartTime,DateTime EndTime,int? SubscriptionId,bool HasDriver=false);
public sealed record AssignmentRequest(int? DriverStaffId);
public sealed record InspectionRequest([param:Range(1,int.MaxValue)]int StaffId,bool IsOk,[param:Range(0,double.MaxValue)]decimal? OdometerKm,[param:Range(0,100)]decimal? FuelBatteryLevel,[param:StringLength(500)]string? DamageNotes,string? ImageUrls);
public sealed record PlanRequest([param:Required] string Name,string? Description,[param:Range(0.01,double.MaxValue)]decimal Price,[param:Range(1,3650)]int DurationDays,[param:Range(0,double.MaxValue)]decimal? IncludedHours,bool IsActive=true){public SubscriptionPlan ToModel()=>new(0,Name,Description,Price,DurationDays,IncludedHours,IsActive);}
public sealed record MaintenanceRequest([param:Range(1,int.MaxValue)]int VehicleId,string? Status,string? MaintenanceType,DateOnly? ScheduledDate,bool? IsSafe,string? Description,[param:Range(0,double.MaxValue)]decimal? Cost){public Maintenance ToModel()=>new(0,VehicleId,Status,MaintenanceType,ScheduledDate,IsSafe,Description,Cost);}
