using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authorization;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// Documents the JSON envelope applied to success and error responses at runtime.
/// </summary>
public sealed class ApiSuccessResponseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var controller = context.MethodInfo.DeclaringType?.Name;
        var action = context.MethodInfo.Name;
        if(controller=="InspectionsController")
            operation.Description="Admin/Manager/CustomerService/Maintenance only. Lists all persisted pre/post-rental inspections including previous vehicles and closed rentals, with recorded inspectorEmail, vehicleName/licensePlate, evidence/imageUrls and time. Optional inspectionType pre_rental/post_rental and positive rentalId combine before counting. Pagination defaults page=1/pageSize=20; page>=1, pageSize 1..100, invalid input returns 400 at HTTP/Application. data has items/page/pageSize/totalCount, out-of-range pages keep count with empty items. Stable order inspectedAt DESC NULLS LAST then id DESC. Pending work comes from rental operations queues, never fabricated inspection rows.";
        if(controller=="OperationsController" && action is "Vehicles" or "Vehicle")
            operation.Description=(operation.Description??"")+" maintenanceTechnicianEmails is a deduplicated sorted array of active Maintenance emails assigned to current non-cancelled open/scheduled/in_progress or unsafe maintenance; completed-safe historical work is excluded. Employee emails are returned only to Admin/Manager/Maintenance; other callers receive an empty array.";
        if (controller == "StaffController" && action == "CreateFromUser")
            operation.Description = "Admin only. Select an active existing user by userId; creates only the employee profile and adds the Staff marker, preserves existing roles, password, email and account identity. isDriver is derived from the stored Driver role; a driver license number and expiry are required only for Driver. Non-driver license metadata is not stored. Positive department/facility references must exist. Missing/inactive account returns 404; duplicate employee profile returns 409. HTTP and Application validate IDs; invalid license returns 400 validation. Account sessions are revoked after profile creation.";
        if (controller == "AccountController" && action == "GetProfile")
            operation.Description = "Any authenticated active account reads its own profile, including nullable staffId/departmentId/departmentName/facilityId/facilityName from persisted employee assignments. No staff directory permission is needed; unassigned facilityName is null.";
        if (controller == "InspectionAssignmentsController")
            operation.Description = "GET: Admin/Manager/CustomerService/Maintenance reads the persisted assignment for the current rental vehicle; data is null if unassigned. PUT: Admin/Manager only, technicianStaffId is required and positive. Target must be active Maintenance. Only pending/pre_inspection before pre-rental evidence; assignment does not replace inspection or change rental status. Missing rental returns 404, invalid technician 400, locked stage/evidence 409. Actor comes from authentication.";
        if (controller == "OperationsController" && action == "Rental")
            operation.Description = "Authorized rental detail in the flat Rental contract. hasPassingPreInspection and hasPostInspection are boolean readiness flags derived only from persisted inspections for this rental's current vehicle; a successful pre-rental check is mandatory before handover. These fields do not replace server-side lifecycle validation.";
        if (controller is "UsersController" or "DashboardController" or "InspectionsController" ||
            (controller == "OperationsController" && action is "Vehicles" or "StaffRatings" or "MaintenanceList" or "DriverSchedule" or "Facilities") ||
            (controller == "DispatchController" && action == "Reports"))
        {
            operation.Description = (operation.Description ?? "") + " Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.";
            foreach (var parameter in operation.Parameters.Where(p => p.Name.Equals("page", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("pageSize", StringComparison.OrdinalIgnoreCase)))
            {
                var size = parameter.Name.Equals("pageSize", StringComparison.OrdinalIgnoreCase);
                parameter.Schema.Minimum = 1;
                parameter.Schema.Default = new Microsoft.OpenApi.Any.OpenApiInteger(size ? 20 : 1);
                if (size) parameter.Schema.Maximum = 100;
            }
        }
        if (controller == "DashboardController")
            operation.Description += " Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.";
        if (controller == "UsersController")
            operation.Description += " Admin only. Users include full roles and nullable customerId/staffId links. Account and role changes invalidate sessions and retain audit. The last active administrator is protected.";
        if (controller == "RentalQueriesController" && action is "Mine" or "MineDetail")
            operation.Description += " Customer fields include persisted facility {id,name,phone}, subscriptionHoursUsed and list/detail-aligned canCancel/nextAction; unknown contact and vehicle metadata remain null.";
        if (controller == "MapController")
        {
            operation.Description = "Mapbox provider. Missing configuration returns 503, upstream failures 502/503, no route 404. No simulated coordinates or ETA. Provider IDs are Mapbox IDs.";
            if (action == "Places")
            {
                operation.Description += " Vietnam address search via Geocoding v6, not POI search. Provider returns at most 10 matches; items/page/pageSize/totalCount paginate that match set, not the global catalog. Defaults page=1/pageSize=20; page>=1, pageSize 1..100. lat/lng must be supplied together; radius requires both and filters by meters.";
                foreach (var parameter in operation.Parameters.Where(p => p.Name is "page" or "pageSize"))
                {
                    parameter.Schema.Minimum = 1;
                    parameter.Schema.Default = new Microsoft.OpenApi.Any.OpenApiInteger(parameter.Name == "page" ? 1 : 20);
                    if (parameter.Name == "pageSize") parameter.Schema.Maximum = 100;
                }
            }
        }
        if (controller == "PaymentsController" && action == "ConfirmCash")
            operation.Description = "Admin/Manager/CustomerService confirms actual full cash receipt for a cash invoice after rental approval. No client amount or paid flag accepted. Atomically records paid ledger with reviewer audit, marks invoice paid and issues the official rental order. Repeated confirmation returns the same receipt/order. Online invoices, cancelled rentals and unapproved requests return 409.";
        if (controller == "PaymentsController" && action == "Checkout")
            operation.Description = "Customer, own invoice only. Rental invoices require prior staff confirmation (confirmed_at present, rental not pending/cancelled); otherwise 409 RENTAL_CONFIRMATION_REQUIRED, without reserving checkout or calling PayOS. Unpaid overtime invoices remain payable after settlement. Payment must complete before handover.";
        if (controller == "PaymentsController" && action == "Invoice")
            operation.Description = "Invoice detail includes canPay (boolean) and paymentBlockedReason (null, RENTAL_CONFIRMATION_REQUIRED or INVOICE_NOT_PAYABLE). These are computed by the same policy rechecked under lock at checkout; clients must not enable payment until canPay=true.";
        if (controller == "RentalLifecycleController" && action == "Confirm")
            operation.Description = "Admin/Manager/CustomerService confirms a pending rental before payment, writes confirmed_at and advances to pre_inspection. An unpaid direct rental can be confirmed. Legacy pre_inspection without confirmed_at may be explicitly approved here; repeating an existing approval returns 409. Direct rental payment remains mandatory before handover.";
        if (controller == "VehicleSearchController" && action == "Search")
        {
            operation.Description = "Public available-vehicle search. facilityId restricts vehicles to one branch; with hasDriver=true, an active qualified driver at that same branch must be free for the requested window including assignment buffer. Drivers are assigned later, not reserved by search. Pagination defaults to page=1/pageSize=20; page>=1 and pageSize 1..100, invalid values return 400. totalCount is after filtering and remains correct on empty pages.";
        }
        if (controller == "CustomersController" && action == "List")
        {
            operation.Description = "Admin/Manager/CustomerService only. Optional search, isActive and licenseStatus filters combine before pagination and totalCount. licenseStatus is pending, approved, rejected or none; none means no license number submitted, pending means submitted and awaiting review. Omit filters to list all customers including inactive accounts. Pagination defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Results are ordered by customer ID ascending; out-of-range pages return empty items.";
            var status = operation.Parameters.FirstOrDefault(p => string.Equals(p.Name, "LicenseStatus", StringComparison.OrdinalIgnoreCase));
            if (status is not null)
                status.Schema.Enum = new[] { "pending", "approved", "rejected", "none" }
                    .Select(value => (Microsoft.OpenApi.Any.IOpenApiAny)new Microsoft.OpenApi.Any.OpenApiString(value)).ToList();
        }
        if ((controller == "RentalQueriesController" && action is "List" or "Mine") ||
            (controller == "HandoversController" && action == "List"))
        {
            operation.Description = "Pagination defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid pagination returns 400. data contains items/page/pageSize/totalCount with the count after filtering and before pagination. Results are ordered by rental ID descending; out-of-range pages return empty items.";
            foreach (var parameter in operation.Parameters.Where(p => p.In == ParameterLocation.Query))
            {
                if (parameter.Name == "page")
                {
                    parameter.Schema.Minimum = 1;
                    parameter.Schema.Default = new Microsoft.OpenApi.Any.OpenApiInteger(1);
                }
                if (parameter.Name == "pageSize")
                {
                    parameter.Schema.Minimum = 1;
                    parameter.Schema.Maximum = 100;
                    parameter.Schema.Default = new Microsoft.OpenApi.Any.OpenApiInteger(20);
                }
            }
            if (controller == "HandoversController")
                operation.Description += " Admin/Manager/CustomerService only. status=pending (default), completed or all. Completed handovers include rentals that have since been returned/completed.";
            if (controller == "RentalQueriesController")
                operation.Description += " Each item includes nullable orderId for the official paid rental order; id remains the booking request ID. Omit status (and tab for mine) to include all lifecycle states, including completed and cancelled.";
        }
        if (controller == "RentalQueriesController" && action is "Detail" or "MineDetail")
            operation.Description = "Rental detail includes status, audit timeline/history and nullable top-level orderId. id (or rental.id) remains the booking request ID; only a persisted official order supplies orderId. A cancelled rental remains cancelled even if a historical paid order exists.";
        if (controller == "RentalEditingController")
            operation.Description = "Admin/Manager/CustomerService only. Updates vehicleId/startTime/endTime/hasDriver before rental start (pending or pre_inspection, started_at null). Times must be UTC. Immutable customer/type/subscription/policy and server-computed prices cannot be supplied. Conflicting bookings, existing inspection/assignment, signed contracts and committed payments block changes with 409. Unpaid invoice and booking are updated in the same transaction.";
        if (controller == "HandoversController" && action == "Confirm")
            operation.Description = "Admin/Manager/CustomerService only. Confirms successful delivery using the rental ID, with no body. Requires pre_inspection, a passing inspection for the current vehicle, valid assignment, and an approved non-expired license for self-drive. Atomically starts the rental (renting) and records history; repeated confirmation returns 409.";
        if (context.MethodInfo.DeclaringType?.Name == "OperationsController" && context.MethodInfo.Name == "Policies")
        {
            operation.Summary = "List rental policy versions with rental usage counts (Admin/Manager).";
            operation.Description = "Pagination defaults to page=1/pageSize=20 (page>=1, pageSize 1..100); data contains items/page/pageSize/totalCount. rentalCount counts all rentals referencing that exact policy, including cancelled rentals; results are ordered by ID descending.";
        }
        if (context.MethodInfo.DeclaringType?.Name == "OperationsController" && context.MethodInfo.Name == "AddPolicy")
        {
            operation.Description = "Creates a policy only when its vehicleTypeId exists (null means global) and no active policy exists in the same scope. Validation failures use error.errors.VehicleTypeId. New policies have rentalCount=0.";
            if (context.SchemaRepository.Schemas.TryGetValue("PolicyRequest", out var request) && request.Properties.TryGetValue("vehicleTypeId", out var typeId)) typeId.Minimum = 1;
        }
        if (context.MethodInfo.DeclaringType?.Name == "OperationsController" && context.MethodInfo.Name == "SetPolicyStatus")
        {
            operation.Summary = "Set rental policy active status (Admin/Manager).";
            operation.Description = "Sets only isActive; policy terms and existing rental snapshots remain unchanged. Repeating the same status is idempotent. Activation is rejected if another active policy exists in the same scope. rentalCount is preserved in the response.";
        }
        foreach (var code in new[] { "400", "404", "405", "500" })
            operation.Responses.TryAdd(code, new OpenApiResponse());

        var attributes = context.MethodInfo.GetCustomAttributes(true)
            .Concat(context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? []);
        if (attributes.OfType<Microsoft.AspNetCore.Mvc.ConsumesAttribute>().Any())
            operation.Responses.TryAdd("415", new OpenApiResponse());
        if (attributes.OfType<AuthorizeAttribute>().Any() && !attributes.OfType<AllowAnonymousAttribute>().Any())
            foreach (var code in new[] { "401", "403" })
                operation.Responses.TryAdd(code, new OpenApiResponse());

        foreach (var (statusCode, response) in operation.Responses.ToList())
        {
            if (!int.TryParse(statusCode, out var numericStatusCode) || numericStatusCode < 200 || numericStatusCode is >= 300 and < 400)
                continue;

            var documentedStatusCode = numericStatusCode == StatusCodes.Status204NoContent
                ? StatusCodes.Status200OK.ToString()
                : statusCode;

            if (documentedStatusCode != statusCode)
            {
                operation.Responses.Remove(statusCode);
                operation.Responses[documentedStatusCode] = response;
            }

            var successful = numericStatusCode < 300;
            response.Description = successful
                ? "Successful response using the six-field API envelope."
                : "Error response using the six-field API envelope; error contains ProblemDetails.";
            response.Content ??= new Dictionary<string, OpenApiMediaType>();
            if (!successful)
            {
                var errorSchema = response.Content.Values.FirstOrDefault()?.Schema
                    ?? context.SchemaGenerator.GenerateSchema(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails), context.SchemaRepository);
                response.Content.Clear();
                response.Content["application/json"] = new OpenApiMediaType
                {
                    Schema = CreateEnvelopeSchema(null, errorSchema, int.Parse(documentedStatusCode), false)
                };
                continue;
            }

            if (response.Content.Count == 0)
                response.Content["application/json"] = new OpenApiMediaType();
            foreach (var mediaType in response.Content.Values)
                mediaType.Schema = CreateEnvelopeSchema(mediaType.Schema, null, int.Parse(documentedStatusCode), true);
        }
    }

    private static OpenApiSchema CreateEnvelopeSchema(OpenApiSchema? dataSchema, OpenApiSchema? errorSchema, int code, bool successful) => new()
    {
        Type = "object",
        Required = new HashSet<string> { "code", "status", "is_success", "data", "message", "error" },
        Properties = new Dictionary<string, OpenApiSchema>
        {
            ["code"] = new() { Type = "integer", Format = "int32", Example = new Microsoft.OpenApi.Any.OpenApiInteger(code) },
            ["status"] = new() { Type = "string", Enum = [new Microsoft.OpenApi.Any.OpenApiString(successful ? "success" : "error")] },
            ["is_success"] = new() { Type = "boolean", Example = new Microsoft.OpenApi.Any.OpenApiBoolean(successful) },
            ["data"] = dataSchema ?? new OpenApiSchema { Nullable = true },
            ["message"] = new() { Type = "string", Example = new Microsoft.OpenApi.Any.OpenApiString(successful ? "Request completed successfully." : "Request failed.") },
            ["error"] = errorSchema ?? new OpenApiSchema { Nullable = true }
        }
    };
}
