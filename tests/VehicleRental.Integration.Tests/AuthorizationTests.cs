using Car_Rental_System_BE.WebApi.Features.Assignments;
using Car_Rental_System_BE.WebApi.Features.Billing;
using Car_Rental_System_BE.WebApi.Features.Customers;
using Car_Rental_System_BE.WebApi.Features.Inspections;
using Car_Rental_System_BE.WebApi.Features.Maintenances;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Features.Fleet;
using Car_Rental_System_BE.WebApi.Features.Organization;
using Car_Rental_System_BE.WebApi.Features.Ratings;
using Car_Rental_System_BE.WebApi.Features.Recovery;
using Car_Rental_System_BE.WebApi.Features.RentalPolicies;
using Car_Rental_System_BE.WebApi.Features.Reports;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Domain.Services;
using Car_Rental_System_BE.Infrastructure.Services.Authentication;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

public sealed class AuthorizationTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Manager", true)]
    [InlineData("Staff", false)]
    [InlineData("Customer", false)]
    [InlineData("Driver", false)]
    public async Task Department_assignment_requires_management(string role, bool allowed)
    {
        Assert.Equal(allowed, await AuthorizeEndpoint<DepartmentMembersController>("Assign", Principal(role)));
        Assert.Equal(allowed, await AuthorizeEndpoint<FacilityMembersController>("Assign", Principal(role)));
        Assert.Equal(allowed, await AuthorizeEndpoint<DepartmentsController>("Departments", Principal(role)));
        Assert.Equal(allowed, await AuthorizeEndpoint<StaffController>("List", Principal(role)));
        foreach (var action in new[] { "Create", "CreateFromUser", "Update", "Deactivate" })
            Assert.Equal(allowed, await AuthorizeEndpoint<StaffController>(action, Principal(role)));
    }

    [Theory]
    [InlineData("Hourly", "Staff", false)]
    [InlineData("Hourly", "Customer", true)]
    [InlineData("SaveContract", "Customer", false)]
    [InlineData("SaveContract", "Driver", false)]
    [InlineData("SaveContract", "Maintenance", false)]
    [InlineData("SaveContract", "CustomerService", true)]
    [InlineData("SaveContract", "Manager", true)]
    [InlineData("SaveContract", "Admin", true)]
    [InlineData("DriverSchedule", "CustomerService", false)]
    [InlineData("DriverSchedule", "Driver", true)]
    [InlineData("DeletePlan", "CustomerService", false)]
    [InlineData("DeletePlan", "Manager", true)]
    public async Task Operations_enforce_capability_matrix(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<OperationsController>(action, Principal(role, 7)));

    /// <summary>
    /// The three rental-policy actions moved to RentalPoliciesController and keep the Management policy they had
    /// on OperationsController, so a policy left behind on the legacy controller cannot pass this matrix.
    /// </summary>
    [Theory]
    [InlineData("Policies", "CustomerService", false)]
    [InlineData("Policies", "Manager", true)]
    public async Task Rental_policy_endpoints_enforce_the_attributes_they_moved_with(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<RentalPoliciesController>(action, Principal(role, 7)));

    /// <summary>
    /// The driver-license review moved to CustomerLicensesController. It is looked up on the controller that
    /// actually owns it now, so a policy left behind on OperationsController cannot pass this matrix.
    /// </summary>
    [Theory]
    [InlineData("ReviewLicense", "Driver", false)]
    [InlineData("ReviewLicense", "CustomerService", true)]
    public async Task Customer_license_review_enforces_the_policy_it_moved_with(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<CustomerLicensesController>(action, Principal(role, 7)));

    /// <summary>
    /// The vehicle actions moved to VehiclesController. They are looked up on the controller that actually owns
    /// them now, so a policy left behind on OperationsController cannot pass this matrix.
    /// </summary>
    [Theory]
    [InlineData("AddVehicle", "Driver", false)]
    [InlineData("AddVehicle", "Staff", false)]
    [InlineData("AddVehicle", "CustomerService", false)]
    [InlineData("AddVehicle", "Manager", true)]
    public async Task Vehicles_enforce_capability_matrix(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<VehiclesController>(action, Principal(role, 7)));

    /// <summary>
    /// The vehicle-type actions moved to VehicleTypesController. Same rule: the policy is asserted on the
    /// controller that owns the action.
    /// </summary>
    [Theory]
    [InlineData("DeleteType", "CustomerService", false)]
    [InlineData("DeleteType", "Manager", true)]
    public async Task Vehicle_types_enforce_capability_matrix(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<VehicleTypesController>(action, Principal(role, 7)));

    /// <summary>
    /// The facility actions moved to FacilitiesController. They are looked up on the controller that actually
    /// owns them now, so a policy left behind on OperationsController cannot pass this matrix.
    /// </summary>
    [Theory]
    [InlineData("AddFacility", "Staff", false)]
    [InlineData("AddFacility", "Manager", true)]
    [InlineData("EditFacility", "Manager", true)]
    [InlineData("DeleteFacility", "Manager", true)]
    public async Task Facility_endpoints_enforce_the_policies_they_moved_with(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<FacilitiesController>(action, Principal(role, 7)));

    /// <summary>
    /// The three maintenance actions moved to MaintenanceController. They are looked up on the controller that
    /// actually owns them now, so a policy left behind on OperationsController cannot pass this matrix.
    /// </summary>
    [Theory]
    [InlineData("MaintenanceList", "Driver", false)]
    [InlineData("MaintenanceList", "CustomerService", false)]
    [InlineData("MaintenanceList", "Maintenance", true)]
    [InlineData("MaintenanceList", "Manager", true)]
    [InlineData("AddMaintenance", "Driver", false)]
    [InlineData("AddMaintenance", "CustomerService", false)]
    [InlineData("AddMaintenance", "Maintenance", false)]
    [InlineData("AddMaintenance", "Manager", true)]
    [InlineData("EditMaintenance", "Driver", false)]
    [InlineData("EditMaintenance", "Maintenance", true)]
    [InlineData("EditMaintenance", "Manager", true)]
    public async Task Maintenance_endpoints_enforce_the_policies_they_moved_with(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<MaintenanceController>(action, Principal(role, 7)));

    [Theory]
    [InlineData("CreateShift", "Manager", true)]
    [InlineData("UpdateShift", "Driver", false)]
    [InlineData("CreateShift", "CustomerService", false)]
    public async Task Dispatch_endpoints_enforce_roles(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<DriverShiftsController>(action, Principal(role, 7)));

    /// <summary>
    /// The candidate check of a replacement moved to InspectionRecordsController; it keeps the Inspection
    /// policy, so a policy left behind on DispatchController cannot pass this matrix.
    /// </summary>
    [Theory]
    [InlineData("InspectReplacement", "Maintenance", true)]
    [InlineData("InspectReplacement", "Manager", true)]
    [InlineData("InspectReplacement", "CustomerService", false)]
    [InlineData("InspectReplacement", "Driver", false)]
    public async Task Inspection_endpoints_enforce_the_attributes_they_moved_with(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<InspectionRecordsController>(action, Principal(role, 7)));

    /// <summary>
    /// Vehicle recovery moved to RecoveryController with the Management policy it had on DispatchController.
    /// </summary>
    [Theory]
    [InlineData("ReplaceVehicle", "Manager", true)]
    [InlineData("ReplaceVehicle", "Driver", false)]
    public async Task Recovery_endpoints_enforce_the_attributes_they_moved_with(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<RecoveryController>(action, Principal(role, 7)));

    /// <summary>
    /// The five incident-report actions moved to ReportsController and keep the attributes they had: two role
    /// lists for filing and reading, the Management policy for assigning, resolving and status. A policy left
    /// behind on DispatchController cannot pass this matrix because the action is looked up where it lives.
    /// </summary>
    [Theory]
    [InlineData("CreateReport", "Driver", true)]
    [InlineData("CreateReport", "Customer", true)]
    [InlineData("Reports", "Driver", true)]
    [InlineData("Reports", "Customer", false)]
    [InlineData("AssignMaintenance", "Manager", true)]
    [InlineData("AssignMaintenance", "Maintenance", false)]
    [InlineData("ResolveReport", "Manager", true)]
    [InlineData("SetReportStatus", "Manager", true)]
    public async Task Report_endpoints_enforce_the_attributes_they_moved_with(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<ReportsController>(action, Principal(role, 7)));

    /// <summary>
    /// The four staff-rating actions moved to RatingsController and keep the attributes they had: the customer
    /// rates a completed rental, management reads the queue and moderates or replies. A policy left behind on
    /// OperationsController cannot pass this matrix because each action is looked up where it lives.
    /// </summary>
    [Theory]
    [InlineData("RateStaff", "Customer", true)]
    [InlineData("StaffRatings", "Manager", true)]
    [InlineData("ModerateRating", "Manager", true)]
    [InlineData("ReplyRating", "Manager", true)]
    public async Task Rating_endpoints_enforce_the_attributes_they_moved_with(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<RatingsController>(action, Principal(role, 7)));

    [Theory]
    [InlineData("Staff", false)]
    [InlineData("Driver", false)]
    [InlineData("Maintenance", false)]
    [InlineData("Customer", false)]
    [InlineData("CustomerService", true)]
    [InlineData("Manager", true)]
    [InlineData("Admin", true)]
    public async Task Financial_endpoints_enforce_specialty(string role, bool allowed)
    {
        // The invoice actions now live in InvoicesController; Refund and Payments stayed in PaymentsController.
        // Each action is looked up on the controller that actually owns it, so a misplaced [Authorize] on
        // either side of the split still fails this matrix.
        foreach (var method in new[] { "Refund", "CreateInvoice", "Invoices", "Invoice", "Payments" })
            Assert.Equal(allowed || (role == "Customer" && method is "Invoices" or "Invoice" or "Payments"),
                await (method is "CreateInvoice" or "Invoices" or "Invoice"
                    ? AuthorizeEndpoint<InvoicesController>(method, Principal(role))
                    : AuthorizeEndpoint<PaymentsController>(method, Principal(role))));
    }

    /// <summary>
    /// The two inspection actions moved to InspectionRecordsController. They are looked up on the controller that
    /// actually owns them now, so the staff-identity assertion of the policy cannot be lost behind the move.
    /// </summary>
    [Theory]
    [InlineData("Staff", false)]
    [InlineData("Driver", false)]
    [InlineData("CustomerService", false)]
    [InlineData("Manager", true)]
    [InlineData("Maintenance", true)]
    [InlineData("Admin", true)]
    public async Task Inspection_requires_technical_role_and_staff_identity(string role, bool allowed)
    {
        Assert.Equal(allowed, await AuthorizeEndpoint<InspectionRecordsController>("Pre", Principal(role, 7)));
        Assert.False(await AuthorizeEndpoint<InspectionRecordsController>("Pre", Principal(role)));
    }

    [Fact]
    public async Task Anonymous_is_denied_even_when_role_claim_exists()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")]));
        Assert.False(await AuthorizeEndpoint<PaymentsController>("Refund", principal));
    }

    [Fact]
    public async Task Inspector_cannot_supply_another_staff_id()
    {
        var controller = InspectionController(Principal("Maintenance", 7), out var calls);
        Assert.IsType<ForbidResult>(await controller.Pre(1, new(8, true, null, null, null, null), default));
        Assert.Empty(calls.Calls);
        await controller.Pre(1, new(null, true, null, null, null, null), default);
        Assert.Equal(7, calls.Calls.Single().Arguments[1]);
    }

    [Fact]
    public async Task Driver_schedule_forces_own_staff_id_and_rejects_other_driver()
    {
        var controller = Controller(Principal("Driver", 7), out var calls);
        var from = DateTime.UtcNow;
        Assert.IsType<ForbidResult>(await controller.DriverSchedule(8, from, from.AddDays(1), new(), default));
        Assert.Empty(calls.Calls);
        await controller.DriverSchedule(null, from, from.AddDays(1), new(), default);
        Assert.Equal(7, calls.Calls.Single().Arguments[0]);
    }

    [Fact]
    public void Jwt_contains_staff_metadata_and_authorization_version()
    {
        var account = new Account(1, "staff@test.local", "Staff", null, null, null, null,
            ["Staff", "Maintenance"], 7, 3, "Technical", 2, 4);
        var service = new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('x', 64) }), Options.Create(new TokenOptions()));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(service.CreateAccessToken(account, "session"));
        Assert.Contains(token.Claims, c => c.Type == "staff_id" && c.Value == "7");
        Assert.Contains(token.Claims, c => c.Type == "department_id" && c.Value == "3");
        Assert.Contains(token.Claims, c => c.Type == "department_name" && c.Value == "Technical");
        Assert.Contains(token.Claims, c => c.Type == "facility_id" && c.Value == "2");
        Assert.Contains(token.Claims, c => c.Type == "authorization_version" && c.Value == "4");
        var user = Principal("Maintenance", 7);
        ((ClaimsIdentity)user.Identity!).AddClaims([new Claim(ClaimTypes.NameIdentifier, "1"), new Claim("authorization_version", "4")]);
        Assert.True(AuthorizationPolicies.IsCurrentAccount(user, account));
        Assert.False(AuthorizationPolicies.IsCurrentAccount(user, account with { AuthorizationVersion = 5 }));
        Assert.False(AuthorizationPolicies.IsCurrentAccount(user, null));
        Assert.False(AuthorizationPolicies.IsCurrentAccount(Principal("Maintenance", 7), account));
        Assert.Equal(3, AccountResponse.From(account).DepartmentId);
    }

    [Theory]
    [InlineData("Admin", false)]
    [InlineData("Manager", false)]
    [InlineData("Driver", false)]
    [InlineData("Maintenance", true)]
    [InlineData("CustomerService", true)]
    public void Staff_payload_cannot_escalate_or_conflict_with_driver_flag(string role, bool driver) =>
        Assert.Throws<ArgumentException>(() => StaffSpecialty.Validate(role, driver));

    private static ClaimsPrincipal Principal(string role, int? staff = null)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.Email, "staff@test.local")], "test");
        if (staff is not null) identity.AddClaim(new Claim("staff_id", staff.ToString()!));
        return new(identity);
    }

    private static async Task<bool> AuthorizeEndpoint<T>(string method, ClaimsPrincipal principal)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization(AuthorizationPolicies.Configure);
        using var provider = services.BuildServiceProvider();
        var metadata = typeof(T).GetCustomAttributes<AuthorizeAttribute>().Concat(typeof(T).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>());
        var policy = await AuthorizationPolicy.CombineAsync(provider.GetRequiredService<IAuthorizationPolicyProvider>(), metadata);
        return (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, policy!)).Succeeded;
    }

    private static InspectionRecordsController InspectionController(ClaimsPrincipal principal, out RepositorySpy spy)
    {
        var records = DispatchProxy.Create<IInspectionRecordRepository, RepositorySpy>();
        spy = (RepositorySpy)records;
        return new(new InspectionRecordService(records)) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = principal } } };
    }

    private static OperationsController Controller(ClaimsPrincipal principal, out RepositorySpy spy)
    {
        var store = DispatchProxy.Create<IOperationsRepository, RepositorySpy>();
        spy = (RepositorySpy)store;
        var completion = DispatchProxy.Create<IOperationsCompletionRepository, RepositorySpy>();
        ((RepositorySpy)completion).Calls = spy.Calls;
        return new(new Car_Rental_System_BE.Application.Services.OperationsService(store), new Car_Rental_System_BE.Application.Services.OperationsCompletionService(completion)) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = principal } } };
    }

    public class RepositorySpy : DispatchProxy
    {
        public List<(string Method, object?[] Arguments)> Calls { get; set; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add((targetMethod!.Name, args!));
            return targetMethod.Name switch
            {
                "AddInspectionAsync" => Task.FromResult(new Inspection(1, 1, "pre_rental", true, null, null, null, null, null)),
                "ListDriverScheduleAsync" => Task.FromResult<IReadOnlyList<DriverScheduleEntry>>([]),
                "DriverScheduleAsync" => Task.FromResult(new OperationsPage<DriverScheduleEntry>([],1,20,0)),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
        }
    }
}
