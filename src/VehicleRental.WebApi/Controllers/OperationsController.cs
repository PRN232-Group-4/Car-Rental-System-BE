using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Requests;
using Car_Rental_System_BE.Domain.Entities;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api")]
public sealed class OperationsController(IOperationsService store, IMediaService media, IOperationsCompletionService? completion = null) : ControllerBase
{
    /// <summary>Admin, Manager or CustomerService records signed long-term contract evidence.</summary>
    [Authorize(Policy=AuthorizationPolicies.CustomerService)]
    [HttpPut("rentals/{id:int}/contract")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveContract(int id, RentalContractRequest request, CancellationToken ct) =>
        Ok(await store.SaveRentalContractAsync(id, request.ContractLink, request.SignedAt, ct));

    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpGet("customer/license")] public async Task<IActionResult> License(CancellationToken ct) => Ok(await store.GetLicenseAsync(Email(),ct));
    /// <summary>Customer uploads and resubmits their driver license for review.</summary>
    [Authorize(Policy=AuthorizationPolicies.Customer)]
    [HttpPut("customer/license")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<CustomerLicense>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveLicense([FromForm] LicenseUploadForm request, CancellationToken ct)
    {
        if (!ImageFileRules.IsImage(request.File, 5 * 1024 * 1024))
            return Problem("License must be an image no larger than 5 MB.", statusCode: StatusCodes.Status400BadRequest);
        await using var content = request.File!.OpenReadStream();
        return Ok(await media.SaveCustomerLicenseAsync(Email(), request.ToModel(), content, request.File.FileName, ct));
    }

    /// <summary>Customer submits an existing image URL using the previous JSON contract.</summary>
    [Authorize(Policy=AuthorizationPolicies.Customer)]
    [HttpPut("customer/license/url")]
    [Consumes("application/json")]
    [ProducesResponseType<CustomerLicense>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveLicenseUrl(LicenseRequest x, CancellationToken ct) =>
        Ok(await store.SaveLicenseAsync(Email(), new(x.Number.Trim(), x.Type.Trim(), x.IssueDate, x.ExpiryDate, x.ImageUrl, "pending") { Id = x.LicenseId }, ct));
    [Authorize(Policy=AuthorizationPolicies.CustomerService)] [HttpPut("customers/{customerId:int}/license-status")] public Task<CustomerLicense> ReviewLicense(int customerId,LicenseReviewRequest x,CancellationToken ct) => store.ReviewLicenseAsync(customerId,x.Status.ToLowerInvariant(),ct,x.LicenseId);

    [HttpGet("vehicle-types")] public Task<IReadOnlyList<VehicleType>> VehicleTypes(CancellationToken ct)=>store.ListVehicleTypesAsync(ct);
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("vehicle-types")] public async Task<IActionResult> AddType(VehicleTypeRequest x,CancellationToken ct){var t=await store.SaveVehicleTypeAsync(null,x.Name,x.FuelType,ct,x.Type);return Created($"api/vehicle-types/{t.Id}",t);}
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("vehicle-types/{id:int}")] public Task<VehicleType> EditType(int id,VehicleTypeRequest x,CancellationToken ct)=>store.SaveVehicleTypeAsync(id,x.Name,x.FuelType,ct,x.Type);
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("vehicle-types/{id:int}")] public async Task<IActionResult> DeleteType(int id,CancellationToken ct){await store.DeleteVehicleTypeAsync(id,ct);return NoContent();}
    [HttpGet("vehicles")] public async Task<VehiclePage> Vehicles([FromQuery]int? vehicleTypeId,[FromQuery]int? facilityId,[FromQuery]string? status,[FromQuery]DateTime? from,[FromQuery]DateTime? to,[FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)
    {
        var result=await store.SearchVehiclesAsync(vehicleTypeId,facilityId,status,from,to,page,pageSize,ct);
        return result with {Items=result.Items.Select(VehicleForCaller).ToArray()};
    }
    [ProducesResponseType<Vehicle>(StatusCodes.Status200OK)]
    [HttpGet("vehicles/{id:int}")] public async Task<IActionResult> Vehicle(int id,CancellationToken ct)=>await store.GetVehicleAsync(id,ct) is { } x?Ok(VehicleForCaller(x)):Problem("Vehicle was not found.", statusCode: StatusCodes.Status404NotFound);
    private Vehicle VehicleForCaller(Vehicle vehicle)=>User.Identity?.IsAuthenticated==true && (AuthorizationPolicies.CanManage(User)||User.IsInRole("Maintenance"))
        ?vehicle:vehicle with {MaintenanceTechnicianEmails=[]};
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("vehicles")] public async Task<IActionResult> AddVehicle(VehicleRequest x,CancellationToken ct){var v=await store.SaveVehicleAsync(null,x.ToModel(),ct);return Created($"api/vehicles/{v.Id}",v);}
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("vehicles/{id:int}")] public async Task<IActionResult> DeleteVehicle(int id,CancellationToken ct){await store.DeleteVehicleAsync(id,ct);return NoContent();}
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("vehicles/{id:int}")] public Task<Vehicle> EditVehicle(int id,VehicleRequest x,CancellationToken ct)=>store.SaveVehicleAsync(id,x.ToModel(),ct);

    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpGet("departments")] public Task<IReadOnlyList<OrganizationUnit>> Departments(CancellationToken ct)=>store.ListDepartmentsAsync(ct);
    [Authorize(Roles="Admin")] [HttpPost("departments")] public async Task<IActionResult> AddDepartment(NameRequest x,CancellationToken ct){var d=await store.SaveDepartmentAsync(null,x.Name,ct);return Created($"api/departments/{d.Id}",d);}
    [Authorize(Roles="Admin")] [HttpPut("departments/{id:int}")] public Task<OrganizationUnit> EditDepartment(int id,NameRequest x,CancellationToken ct)=>store.SaveDepartmentAsync(id,x.Name,ct);
    [Authorize(Roles="Admin")] [HttpDelete("departments/{id:int}")] public async Task<IActionResult> DeleteDepartment(int id,CancellationToken ct){await store.DeleteDepartmentAsync(id,ct);return NoContent();}
    [HttpGet("facilities")] [ProducesResponseType<OperationsPage<OrganizationUnit>>(StatusCodes.Status200OK)] public Task<OperationsPage<OrganizationUnit>> Facilities([FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)=>store.ListFacilitiesAsync(page,pageSize,ct);
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("facilities")] public async Task<IActionResult> AddFacility(FacilityRequest x,CancellationToken ct){var f=await store.SaveFacilityAsync(null,x.Name,x.Address,ct,x.Phone);return Created($"api/facilities/{f.Id}",f);}
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("facilities/{id:int}")] public Task<OrganizationUnit> EditFacility(int id,FacilityRequest x,CancellationToken ct)=>store.SaveFacilityAsync(id,x.Name,x.Address,ct,x.Phone);
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("facilities/{id:int}")] public async Task<IActionResult> DeleteFacility(int id,CancellationToken ct){await store.DeleteFacilityAsync(id,ct);return NoContent();}

    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpGet("rental-policies")]
    [ProducesResponseType<RentalPolicyPage>(StatusCodes.Status200OK)]
    public Task<RentalPolicyPage> Policies([FromQuery, Range(1, int.MaxValue)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default) =>
        store.ListPoliciesAsync(page, pageSize, ct);

    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpPost("rental-policies")]
    [ProducesResponseType<RentalPolicy>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddPolicy(PolicyRequest x, CancellationToken ct)
    {
        try
        {
            var policy = await store.CreatePolicyAsync(x.ToModel() with { ManagedBy = AuthorizationPolicies.StaffId(User) }, ct);
            return Created($"api/rental-policies/{policy.Id}", policy);
        }
        catch (ValidationException error)
        {
            var fields = error.ValidationResult?.MemberNames ?? [];
            foreach (var field in fields.DefaultIfEmpty("")) ModelState.AddModelError(field, error.Message);
            return ValidationProblem(ModelState);
        }
    }

    /// <summary>Admin/Manager đặt trạng thái áp dụng; giữ nguyên điều khoản và giá của các đơn cũ.</summary>
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpPatch("rental-policies/{id:int}/status")]
    [ProducesResponseType<RentalPolicy>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RentalPolicy> SetPolicyStatus([FromRoute, Range(1, int.MaxValue)] int id, PolicyStatusRequest request, CancellationToken ct) =>
        store.SetPolicyStatusAsync(id, request.IsActive!.Value, ct);

    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpPost("rentals/hourly")] public async Task<IActionResult> Hourly(RentalRequest x,CancellationToken ct)=>Created("api/rentals",await store.CreateRentalAsync(Email(),x.VehicleId,x.PolicyId,"hourly",x.StartTime,x.EndTime,x.SubscriptionId is not null,x.SubscriptionId,x.HasDriver,ct,x.PaymentMethod));
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpPost("rentals/longterm")] public async Task<IActionResult> Longterm(RentalRequest x,CancellationToken ct)=>Created("api/rentals",await store.CreateRentalAsync(Email(),x.VehicleId,x.PolicyId,"longterm",x.StartTime,x.EndTime,x.SubscriptionId is not null,x.SubscriptionId,x.HasDriver,ct,x.PaymentMethod));
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [Authorize(Policy=AuthorizationPolicies.RentalRead)] [HttpGet("rentals/{id:int}")] public async Task<IActionResult> Rental(int id,CancellationToken ct)=>await store.GetRentalForUserAsync(id,Email(),AuthorizationPolicies.CanReadAllRentals(User),User.IsInRole("Customer"),User.IsInRole("Driver"),ct) is { } x?Ok(x):Problem("Rental was not found.", statusCode: StatusCodes.Status404NotFound);
    [Obsolete("Use POST /api/rentals/{id}/assignments/driver instead.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("rentals/{id:int}/assignments")] public async Task<IActionResult> Assign(int id,AssignmentRequest x,CancellationToken ct){Response.Headers.Append("Deprecation","true");await store.AssignAsync(id,x.DriverStaffId,Email(),ct);return NoContent();}
    [Authorize(Policy=AuthorizationPolicies.DriverSchedule)] [HttpGet("drivers/schedule")] [ProducesResponseType<OperationsPage<DriverScheduleEntry>>(StatusCodes.Status200OK)] [ProducesResponseType(StatusCodes.Status400BadRequest)] public async Task<IActionResult> DriverSchedule([FromQuery]int? driverStaffId,[FromQuery]DateTime from,[FromQuery]DateTime to,[FromQuery]OperationsPagination pagination,CancellationToken ct){if(to<=from)return Problem("End time must be after start time.",statusCode:StatusCodes.Status400BadRequest);if(!AuthorizationPolicies.CanManage(User)){var own=AuthorizationPolicies.StaffId(User);if(own is null || (driverStaffId is not null && driverStaffId!=own))return Forbid();driverStaffId=own;}return Ok(await Completion.DriverScheduleAsync(driverStaffId,from,to,pagination,ct));}
    [Authorize(Policy=AuthorizationPolicies.Inspection)] [HttpPost("rentals/{id:int}/inspections/pre")] public Task<IActionResult> Pre(int id,InspectionRequest x,CancellationToken ct)=>Inspect(id,x,"pre_rental",ct);
    [Authorize(Policy=AuthorizationPolicies.Inspection)] [HttpPost("rentals/{id:int}/inspections/post")] public Task<IActionResult> Post(int id,InspectionRequest x,CancellationToken ct)=>Inspect(id,x,"post_rental",ct);
    [Obsolete("Use dedicated lifecycle endpoints: POST /api/rentals/{id}/confirm, start, return, complete, cancel instead.")]
    [Authorize(Policy=AuthorizationPolicies.CustomerService)]
    [HttpPost("rentals/{id:int}/status/{status}")]
    public async Task<Rental> Status(int id,string status,CancellationToken ct)
    {
        Response.Headers.Append("Deprecation","true");
        return await store.ChangeRentalStatusAsync(id,status,ct);
    }

    [HttpGet("subscription-plans")] public Task<IReadOnlyList<SubscriptionPlan>> Plans([FromQuery]bool all,CancellationToken ct)=>store.ListPlansAsync(!(all && AuthorizationPolicies.CanManage(User)),ct);
    [HttpGet("subscription-plans/{id:int}")] public async Task<IActionResult> Plan(int id,CancellationToken ct)=>await store.GetPlanAsync(id,ct) is { } x?Ok(x):Problem("Subscription plan was not found.", statusCode: StatusCodes.Status404NotFound);
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("subscription-plans")] public async Task<IActionResult> AddPlan(PlanRequest x,CancellationToken ct){var p=await store.SavePlanAsync(null,x.ToModel(),ct);return Created($"api/subscription-plans/{p.Id}",p);}
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("subscription-plans/{id:int}")] public Task<SubscriptionPlan> EditPlan(int id,PlanRequest x,CancellationToken ct)=>store.SavePlanAsync(id,x.ToModel(),ct);
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("subscription-plans/{id:int}")] public async Task<IActionResult> DeletePlan(int id,CancellationToken ct){await store.DeletePlanAsync(id,ct);return NoContent();}
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpGet("customer-subscriptions")] public Task<IReadOnlyList<CustomerSubscription>> CustomerSubscriptions(CancellationToken ct)=>store.ListCustomerSubscriptionsAsync(Email(),ct);
    /// <summary>List maintenance with optional createdAt range [from,to); page=1/pageSize=20, maximum 100.</summary>
    [Authorize(Policy=AuthorizationPolicies.Maintenance)] [HttpGet("maintenance")]
    [ProducesResponseType<OperationsPage<Maintenance>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<OperationsPage<Maintenance>> MaintenanceList([FromQuery]int? vehicleId,[FromQuery]string? status,[FromQuery]DateTime? from,[FromQuery]DateTime? to,[FromQuery]OperationsPagination pagination,CancellationToken ct)
        => Completion.MaintenanceAsync(vehicleId,status,from,to,pagination,ct);
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("maintenance")] public async Task<IActionResult> AddMaintenance(MaintenanceRequest x,CancellationToken ct){var m=await store.SaveMaintenanceAsync(null,x.ToModel(),ct);return Created($"api/maintenance/{m.Id}",m);}
    [Authorize(Policy=AuthorizationPolicies.Maintenance)] [HttpPut("maintenance/{id:int}")] public async Task<IActionResult> EditMaintenance(int id,MaintenanceRequest x,CancellationToken ct)
    {
        var technician=AuthorizationPolicies.CanManage(User)?null:AuthorizationPolicies.StaffId(User);
        if (!AuthorizationPolicies.CanManage(User) && technician is null) return Forbid();
        return Ok(await store.SaveMaintenanceAsync(id,x.ToModel(),ct,technician));
    }
    private async Task<IActionResult> Inspect(int id, InspectionRequest request, string type, CancellationToken ct)
    {
        var staffId = AuthorizationPolicies.StaffId(User);
        if (staffId is null || (request.StaffId is not null && request.StaffId != staffId)) return Forbid();
        return Ok(await store.AddInspectionAsync(id, staffId.Value, type, request.IsOk,
            request.OdometerKm, request.FuelBatteryLevel, request.DamageNotes, request.ImageUrls, ct));
    }

    [Authorize(Policy=AuthorizationPolicies.Customer)]
    [HttpPost("rentals/{rentalId:int}/ratings")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RateStaff(int rentalId, RateStaffRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var result = await store.RateStaffAsync(rentalId, request.StaffId, request.Rating, request.Comment, Email(), ct);
            return Created($"api/staff/{request.StaffId}/ratings", result);
        }
        catch (KeyNotFoundException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound); }
    }

    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpGet("staff/{staffId:int}/ratings")]
    [ProducesResponseType<OperationsPage<StaffRating>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<OperationsPage<StaffRating>> StaffRatings([Range(1,int.MaxValue)] int staffId, [FromQuery] OperationsPagination pagination, CancellationToken ct) =>
        Completion.RatingsAsync(staffId, pagination, ct);

    /// <summary>Admin/Manager moderates a rating; reason and authenticated actor are retained.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPatch("ratings/{id:int}")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)] [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<StaffRating> ModerateRating([Range(1,int.MaxValue)] int id, RatingModerationRequest request, CancellationToken ct)
        => Completion.ModerateRatingAsync(id,request,Email(),ct);

    /// <summary>Admin/Manager publishes or replaces the management reply to a rating.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("ratings/{id:int}/reply")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)] [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<StaffRating> ReplyRating([Range(1,int.MaxValue)] int id, RatingReplyRequest request, CancellationToken ct)
        => Completion.ReplyRatingAsync(id,request,Email(),ct);

    private IOperationsCompletionService Completion => completion ?? throw new InvalidOperationException("Operations completion service is not configured.");

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
