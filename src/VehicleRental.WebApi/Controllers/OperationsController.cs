using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Middlewares.Requests;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api")]
public sealed class OperationsController(
    IOperationsService store,
    IMediaService media,
    IOperationsCompletionService? completion = null,
    // Only the deprecated POST /api/rental/{id}/status/{status} needs these two; the runtime always registers
    // them (Program.cs/Infrastructure) and they stay optional so hosts and tests that only exercise the other
    // endpoints of this controller keep working unchanged.
    IRentalLifecycleService? lifecycle = null,
    ICurrentCustomer? currentCustomer = null) : ControllerBase
{
    /// <summary>Admin, Manager or CustomerService records signed long-term contract evidence.</summary>
    [SuccessMessage("Signed long-term rental contract recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.CustomerService)]
    [HttpPut("rental/{id:int}/contract")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveContract(int id, RentalContractRequest request, CancellationToken ct) =>
        Ok(await store.SaveRentalContractAsync(id, request.ContractLink, request.SignedAt, ct));

    [OperationDescription("Customer reads their own driver license with id and licenses containing all independently reviewed licenses. An existing customer with a null, empty or whitespace license number has status none, including legacy rows with a pending default. A submitted license retains pending, approved or rejected. Missing customer returns null; reading never changes stored data.")]
    [SuccessMessage("Driver license retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpGet("customer/license")] public async Task<IActionResult> License(CancellationToken ct) => Ok(await store.GetLicenseAsync(Email(),ct));
    /// <summary>Customer uploads and resubmits their driver license for review.</summary>
    [OperationDescription("Customer submits their own driver license for review. Optional licenseId updates an owned record; omitted ID adds or upserts by number, preserving other licenses. Number, type, issueDate, expiryDate and image are all required. Multipart /customer/license requires a new file; JSON /customer/license/url requires a valid imageUrl. Missing values return 400 before upload or persistence. Issue date must not be after expiry and expiry must not be before today UTC. Successful submission resets status to pending.")]
    [SuccessMessage("Driver license saved successfully.")]
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
    [OperationDescription("Customer submits their own driver license for review. Optional licenseId updates an owned record; omitted ID adds or upserts by number, preserving other licenses. Number, type, issueDate, expiryDate and image are all required. Multipart /customer/license requires a new file; JSON /customer/license/url requires a valid imageUrl. Missing values return 400 before upload or persistence. Issue date must not be after expiry and expiry must not be before today UTC. Successful submission resets status to pending.")]
    [SuccessMessage("Driver license saved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)]
    [HttpPut("customer/license/url")]
    [Consumes("application/json")]
    [ProducesResponseType<CustomerLicense>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveLicenseUrl(LicenseRequest x, CancellationToken ct) =>
        Ok(await store.SaveLicenseAsync(Email(), new(x.Number.Trim(), x.Type.Trim(), x.IssueDate, x.ExpiryDate, x.ImageUrl, "pending") { Id = x.LicenseId }, ct));
    [SuccessMessage("Driver license review completed successfully.")]
    [Authorize(Policy=AuthorizationPolicies.CustomerService)] [HttpPut("customer/{customerId:int}/license-status")] public Task<CustomerLicense> ReviewLicense(int customerId,LicenseReviewRequest x,CancellationToken ct) => store.ReviewLicenseAsync(customerId,x.Status.ToLowerInvariant(),ct,x.LicenseId);

    [SuccessMessage("Vehicle types retrieved successfully.")]
    [HttpGet("vehicle-types")] public Task<IReadOnlyList<VehicleType>> VehicleTypes(CancellationToken ct)=>store.ListVehicleTypesAsync(ct);
    [SuccessMessage("Vehicle type created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("vehicle-types")] public async Task<IActionResult> AddType(VehicleTypeRequest x,CancellationToken ct){var t=await store.SaveVehicleTypeAsync(null,x.Name,x.FuelType,ct,x.Type);return Created($"/api/vehicle-type/{t.Id}",t);}
    [SuccessMessage("Vehicle type updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("vehicle-type/{id:int}")] public Task<VehicleType> EditType(int id,VehicleTypeRequest x,CancellationToken ct)=>store.SaveVehicleTypeAsync(id,x.Name,x.FuelType,ct,x.Type);
    [SuccessMessage("Vehicle type deleted successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("vehicle-type/{id:int}")] public async Task<IActionResult> DeleteType(int id,CancellationToken ct){await store.DeleteVehicleTypeAsync(id,ct);return NoContent();}
    [OperationDescription(" maintenanceTechnicianEmails is a deduplicated sorted array of active Maintenance emails assigned to current non-cancelled open/scheduled/in_progress or unsafe maintenance; completed-safe historical work is excluded. Employee emails are returned only to Admin/Manager/Maintenance; other callers receive an empty array.")]
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Default order is created_at DESC, id DESC, applied before pagination; modification time does not change order.")]
    [SuccessMessage("Vehicles retrieved successfully.")]
    [HttpGet("vehicles")] public async Task<VehiclePage> Vehicles([FromQuery]int? vehicleTypeId,[FromQuery]int? facilityId,[FromQuery]string? status,[FromQuery]DateTime? from,[FromQuery]DateTime? to,[FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)
    {
        var result=await store.SearchVehiclesAsync(vehicleTypeId,facilityId,status,from,to,page,pageSize,ct);
        return result with {Items=result.Items.Select(VehicleForCaller).ToArray()};
    }
    [OperationDescription(" maintenanceTechnicianEmails is a deduplicated sorted array of active Maintenance emails assigned to current non-cancelled open/scheduled/in_progress or unsafe maintenance; completed-safe historical work is excluded. Employee emails are returned only to Admin/Manager/Maintenance; other callers receive an empty array.")]
    [SuccessMessage("Vehicle retrieved successfully.")]
    [ProducesResponseType<Vehicle>(StatusCodes.Status200OK)]
    [HttpGet("vehicle/{id:int}")] public async Task<IActionResult> Vehicle(int id,CancellationToken ct)=>await store.GetVehicleAsync(id,ct) is { } x?Ok(VehicleForCaller(x)):Problem("Vehicle was not found.", statusCode: StatusCodes.Status404NotFound);
    private Vehicle VehicleForCaller(Vehicle vehicle)=>User.Identity?.IsAuthenticated==true && (AuthorizationPolicies.CanManage(User)||User.IsInRole("Maintenance"))
        ?vehicle:vehicle with {MaintenanceTechnicianEmails=[]};
    [SuccessMessage("Vehicle created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("vehicles")] public async Task<IActionResult> AddVehicle(VehicleRequest x,CancellationToken ct){var v=await store.SaveVehicleAsync(null,x.ToModel(),ct);return Created($"/api/vehicle/{v.Id}",v);}
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("vehicle/{id:int}")] public async Task<IActionResult> DeleteVehicle(int id,CancellationToken ct){await store.DeleteVehicleAsync(id,ct);return NoContent();}
    [SuccessMessage("Vehicle updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("vehicle/{id:int}")] public Task<Vehicle> EditVehicle(int id,VehicleRequest x,CancellationToken ct)=>store.SaveVehicleAsync(id,x.ToModel(),ct);

    [OperationSummary("List rental policy versions with rental usage counts (Admin/Manager).")]
    [OperationDescription("Pagination defaults to page=1/pageSize=20 (page>=1, pageSize 1..100); data contains items/page/pageSize/totalCount. rentalCount counts all rentals referencing that exact policy, including cancelled rentals; results are ordered by ID descending.")]
    [SuccessMessage("Rental policies retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpGet("rental-policies")]
    [ProducesResponseType<RentalPolicyPage>(StatusCodes.Status200OK)]
    public Task<RentalPolicyPage> Policies([FromQuery, Range(1, int.MaxValue)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default) =>
        store.ListPoliciesAsync(page, pageSize, ct);

    [OperationDescription("Creates a policy only when its vehicleTypeId exists (null means global) and no active policy exists in the same scope. Validation failures use error.errors.VehicleTypeId. New policies have rentalCount=0.")]
    [SchemaPropertyMinimum("PolicyRequest", "vehicleTypeId", 1)]
    [SuccessMessage("Rental policy created successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpPost("rental-policies")]
    [ProducesResponseType<RentalPolicy>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddPolicy(PolicyRequest x, CancellationToken ct)
    {
        try
        {
            var policy = await store.CreatePolicyAsync(x.ToModel() with { ManagedBy = AuthorizationPolicies.StaffId(User) }, ct);
            return Created($"/api/rental-policy/{policy.Id}", policy);
        }
        catch (ValidationException error)
        {
            var fields = error.ValidationResult?.MemberNames ?? [];
            foreach (var field in fields.DefaultIfEmpty("")) ModelState.AddModelError(field, error.Message);
            return ValidationProblem(ModelState);
        }
    }

    /// <summary>Admin/Manager đặt trạng thái áp dụng; giữ nguyên điều khoản và giá của các đơn cũ.</summary>
    [OperationSummary("Set rental policy active status (Admin/Manager).")]
    [OperationDescription("Sets only isActive; policy terms and existing rental snapshots remain unchanged. Repeating the same status is idempotent. Activation is rejected if another active policy exists in the same scope. rentalCount is preserved in the response.")]
    [SuccessMessage("Rental policy status updated successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpPatch("rental-policy/{id:int}/status")]
    [ProducesResponseType<RentalPolicy>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RentalPolicy> SetPolicyStatus([FromRoute, Range(1, int.MaxValue)] int id, PolicyStatusRequest request, CancellationToken ct) =>
        store.SetPolicyStatusAsync(id, request.IsActive!.Value, ct);

    [OperationDescription("Customer creates a rental preserving hasDriver for both hourly and longterm. The pickup time must be at least Rental:Expiry:MinimumLeadMinutes (default 60) ahead of the booking moment; earlier start times return 400 RENTAL_START_TIME_TOO_SOON and nothing is written. The created request stores expire_at = booking time + Rental:Expiry:ApprovalWindowMinutes (default 60) for FR-042 auto-expiry. Only available vehicles without a currently renting/post_inspection rental can be booked; schedule/maintenance checks run inside the transaction under a vehicle lock. Driver bookings automatically record a passing pre-rental confirmation with source driver_default and actor system, without assigning a driver. Self-drive requires an approved customer license matching vehicle allowedLicenseTypes and valid through rental end, plus an explicit technician check or Admin confirmation before handover.")]
    [SuccessMessage("Hourly rental created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpPost("rentals/hourly")] public async Task<IActionResult> Hourly(RentalRequest x,CancellationToken ct)
    {
        var rental = await store.CreateRentalAsync(Email(),x.VehicleId,x.PolicyId,"hourly",x.StartTime,x.EndTime,x.SubscriptionId is not null,x.SubscriptionId,x.HasDriver,ct,x.PaymentMethod);
        return Created($"/api/rental/{rental.Id}", rental);
    }
    [OperationDescription("Customer creates a rental preserving hasDriver for both hourly and longterm. The pickup time must be at least Rental:Expiry:MinimumLeadMinutes (default 60) ahead of the booking moment; earlier start times return 400 RENTAL_START_TIME_TOO_SOON and nothing is written. The created request stores expire_at = booking time + Rental:Expiry:ApprovalWindowMinutes (default 60) for FR-042 auto-expiry. Only available vehicles without a currently renting/post_inspection rental can be booked; schedule/maintenance checks run inside the transaction under a vehicle lock. Driver bookings automatically record a passing pre-rental confirmation with source driver_default and actor system, without assigning a driver. Self-drive requires an approved customer license matching vehicle allowedLicenseTypes and valid through rental end, plus an explicit technician check or Admin confirmation before handover.")]
    [SuccessMessage("Long-term rental created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpPost("rentals/longterm")] public async Task<IActionResult> Longterm(RentalRequest x,CancellationToken ct)
    {
        var rental = await store.CreateRentalAsync(Email(),x.VehicleId,x.PolicyId,"longterm",x.StartTime,x.EndTime,x.SubscriptionId is not null,x.SubscriptionId,x.HasDriver,ct,x.PaymentMethod);
        return Created($"/api/rental/{rental.Id}", rental);
    }
    [OperationDescription("Authorized rental detail in the flat Rental contract. hasPassingPreInspection and hasPostInspection are boolean readiness flags derived only from persisted inspections for this rental's current vehicle; a successful pre-rental check is mandatory before handover. These fields do not replace server-side lifecycle validation.")]
    [SuccessMessage("Rental retrieved successfully.")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [Authorize(Policy=AuthorizationPolicies.RentalRead)] [HttpGet("rental/{id:int}")] public async Task<IActionResult> Rental(int id,CancellationToken ct)=>await store.GetRentalForUserAsync(id,Email(),AuthorizationPolicies.CanReadAllRentals(User),User.IsInRole("Customer"),User.IsInRole("Driver"),ct) is { } x?Ok(x):Problem("Rental was not found.", statusCode: StatusCodes.Status404NotFound);
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Driver schedule retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.DriverSchedule)] [HttpGet("drivers/schedule")] [ProducesResponseType<OperationsPage<DriverScheduleEntry>>(StatusCodes.Status200OK)] [ProducesResponseType(StatusCodes.Status400BadRequest)] public async Task<IActionResult> DriverSchedule([FromQuery]int? driverStaffId,[FromQuery]DateTime from,[FromQuery]DateTime to,[FromQuery]OperationsPagination pagination,CancellationToken ct){if(to<=from)return Problem("End time must be after start time.",statusCode:StatusCodes.Status400BadRequest);if(!AuthorizationPolicies.CanManage(User)){var own=AuthorizationPolicies.StaffId(User);if(own is null || (driverStaffId is not null && driverStaffId!=own))return Forbid();driverStaffId=own;}return Ok(await Completion.DriverScheduleAsync(driverStaffId,from,to,pagination,ct));}
    [SuccessMessage("Pre-rental inspection recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Inspection)] [HttpPost("rental/{id:int}/inspections/pre")] public Task<IActionResult> Pre(int id,InspectionRequest x,CancellationToken ct)=>Inspect(id,x,"pre_rental",ct);
    [SuccessMessage("Post-rental inspection recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Inspection)] [HttpPost("rental/{id:int}/inspections/post")] public Task<IActionResult> Post(int id,InspectionRequest x,CancellationToken ct)=>Inspect(id,x,"post_rental",ct);
    /// <summary>
    /// Deprecated. Kept at its original route, verb, policy and response shape, but it no longer runs a second
    /// state machine: every accepted status is delegated to the single lifecycle implementation
    /// (<see cref="IRentalLifecycleService"/>), which writes rental_status_history, runs the completion settlement
    /// and applies the cancellation fee/refund policy. Because of that, <c>completed</c> now settles the rental,
    /// <c>cancelled</c> now charges the fee, and <c>renting</c> now requires a valid customer licence (self-drive).
    /// Business-rule failures surface as the usual 409 conflict envelope.
    /// </summary>
    [OperationDescription("Deprecated (Deprecation: true header). The route, the POST verb, the CustomerService policy and the flat Rental response shape are unchanged, but this endpoint no longer changes the status on its own: pre_inspection, renting, post_inspection, completed and cancelled delegate to the single rental lifecycle service (confirm, start, return, complete, cancel), which writes rental_status_history. completed therefore runs the full settlement (package hour deduction, overtime/supplementary invoice, deposit refund, loyalty points, formal rental order), cancelled applies the cancellation fee and refund policy, and renting requires an approved, unexpired customer licence for self-drive rentals. Lifecycle business-rule failures return 409 with the lifecycle code (ALREADY_IN_STATE, ILLEGAL_TRANSITION, PAYMENT_REQUIRED, PRE_INSPECTION_REQUIRED, ASSIGNMENT_REQUIRED, CUSTOMER_LICENSE_NOT_VALID, POST_INSPECTION_REQUIRED, SIGNED_CONTRACT_REQUIRED); any other status value returns 400 naming the accepted statuses.")]
    [SuccessMessage("Rental status updated successfully.")]
    [Obsolete("Use dedicated lifecycle endpoints: POST /api/rental/{id}/confirm, start, return, complete, cancel instead.")]
    [Authorize(Policy=AuthorizationPolicies.CustomerService)]
    [HttpPost("rental/{id:int}/status/{status}")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Status(int id, string status, CancellationToken ct)
    {
        Response.Headers.Append("Deprecation","true");
        var actor = await ResolveActorAsync(ct);
        switch (status?.Trim().ToLowerInvariant())
        {
            case "pre_inspection": await Lifecycle.ConfirmAsync(id, actor, ct); break;
            case "renting": await Lifecycle.StartAsync(id, actor, ct); break;
            case "post_inspection": await Lifecycle.ReturnAsync(id, null, actor, ct); break;
            case "completed": await Lifecycle.CompleteAsync(id, actor, ct); break;
            case "cancelled":
                await Lifecycle.CancelAsync(id,
                    new CancelRentalCommand(StaffCancellationReason, Initiator: "staff", WaiveFee: false), actor, ct);
                break;
            default:
                return Problem(
                    $"Unknown rental status '{status}'. This endpoint accepts: pre_inspection, renting, post_inspection, completed, cancelled.",
                    statusCode: StatusCodes.Status400BadRequest);
        }

        // The caller gets the same flat Rental contract as before, reloaded through the operations read path.
        return await store.GetRentalAsync(id, ct) is { } rental
            ? Ok(rental)
            : Problem("Rental was not found.", statusCode: StatusCodes.Status404NotFound);
    }

    [SuccessMessage("Subscription plans retrieved successfully.")]
    [HttpGet("subscription-plans")] public Task<IReadOnlyList<SubscriptionPlan>> Plans([FromQuery]bool all,CancellationToken ct)=>store.ListPlansAsync(!(all && AuthorizationPolicies.CanManage(User)),ct);
    [SuccessMessage("Subscription plan retrieved successfully.")]
    [HttpGet("subscription-plan/{id:int}")] public async Task<IActionResult> Plan(int id,CancellationToken ct)=>await store.GetPlanAsync(id,ct) is { } x?Ok(x):Problem("Subscription plan was not found.", statusCode: StatusCodes.Status404NotFound);
    [SuccessMessage("Subscription plan created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("subscription-plans")] public async Task<IActionResult> AddPlan(PlanRequest x,CancellationToken ct){var p=await store.SavePlanAsync(null,x.ToModel(),ct);return Created($"/api/subscription-plan/{p.Id}",p);}
    [SuccessMessage("Subscription plan updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("subscription-plan/{id:int}")] public Task<SubscriptionPlan> EditPlan(int id,PlanRequest x,CancellationToken ct)=>store.SavePlanAsync(id,x.ToModel(),ct);
    [SuccessMessage("Subscription plan deleted successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("subscription-plan/{id:int}")] public async Task<IActionResult> DeletePlan(int id,CancellationToken ct){await store.DeletePlanAsync(id,ct);return NoContent();}
    [SuccessMessage("Customer subscriptions retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpGet("customer-subscriptions")] public Task<IReadOnlyList<CustomerSubscription>> CustomerSubscriptions(CancellationToken ct)=>store.ListCustomerSubscriptionsAsync(Email(),ct);
    private async Task<IActionResult> Inspect(int id, InspectionRequest request, string type, CancellationToken ct)
    {
        var staffId = AuthorizationPolicies.StaffId(User);
        if (staffId is null || (request.StaffId is not null && request.StaffId != staffId)) return Forbid();
        return Ok(await store.AddInspectionAsync(id, staffId.Value, type, request.IsOk,
            request.OdometerKm, request.FuelBatteryLevel, request.DamageNotes, request.ImageUrls, ct));
    }

    [Authorize(Policy=AuthorizationPolicies.Customer)]
    [HttpPost("rental/{rentalId:int}/ratings")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RateStaff(int rentalId, RateStaffRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var result = await store.RateStaffAsync(rentalId, request.StaffId, request.Rating, request.Comment, Email(), ct);
            return Created($"/api/rating/{result.Id}", result);
        }
        catch (KeyNotFoundException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound); }
    }

    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpGet("staff/{staffId:int}/ratings")]
    [ProducesResponseType<OperationsPage<StaffRating>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<OperationsPage<StaffRating>> StaffRatings([Range(1,int.MaxValue)] int staffId, [FromQuery] OperationsPagination pagination, CancellationToken ct) =>
        Completion.RatingsAsync(staffId, pagination, ct);

    /// <summary>Admin/Manager moderates a rating; reason and authenticated actor are retained.</summary>
    [SuccessMessage("Staff rating moderation status updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPatch("rating/{id:int}")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)] [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<StaffRating> ModerateRating([Range(1,int.MaxValue)] int id, RatingModerationRequest request, CancellationToken ct)
        => Completion.ModerateRatingAsync(id,request,Email(),ct);

    /// <summary>Admin/Manager publishes or replaces the management reply to a rating.</summary>
    [SuccessMessage("Management reply to staff rating saved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("rating/{id:int}/reply")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)] [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<StaffRating> ReplyRating([Range(1,int.MaxValue)] int id, RatingReplyRequest request, CancellationToken ct)
        => Completion.ReplyRatingAsync(id,request,Email(),ct);

    private IOperationsCompletionService Completion => completion ?? throw new InvalidOperationException("Operations completion service is not configured.");

    private IRentalLifecycleService Lifecycle => lifecycle ?? throw new InvalidOperationException("Rental lifecycle service is not configured.");

    private ICurrentCustomer CurrentCustomer => currentCustomer ?? throw new InvalidOperationException("Current customer resolver is not configured.");

    private const string StaffCancellationReason = "Staff set the rental to cancelled through the deprecated status endpoint.";

    /// <summary>
    /// Same mapping as <c>RentalLifecycleController.ResolveActorAsync</c> on purpose: the actor email/role and user
    /// id land in rental_status_history and in the payment audit columns, so the two entry points must not drift.
    /// </summary>
    private async Task<ActorContext> ResolveActorAsync(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Customer";

        int? userId = null;
        if (role == "Customer")
        {
            userId = await CurrentCustomer.GetCustomerIdByEmailAsync(email, ct);
        }
        else if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
        {
            userId = uid;
        }

        return new ActorContext(userId, email, role);
    }

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
