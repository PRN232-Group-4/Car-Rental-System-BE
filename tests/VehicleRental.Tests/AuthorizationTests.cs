using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Organization;
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
        Assert.Equal(allowed, await AuthorizeEndpoint<OperationsController>("Departments", Principal(role)));
        Assert.Equal(allowed, await AuthorizeEndpoint<StaffController>("List", Principal(role)));
        foreach (var action in new[] { "Create", "Update", "Deactivate" })
            Assert.Equal(role == "Admin", await AuthorizeEndpoint<StaffController>(action, Principal(role)));
    }

    [Theory]
    [InlineData("AddVehicle", "Driver", false)]
    [InlineData("AddVehicle", "Staff", false)]
    [InlineData("AddVehicle", "CustomerService", false)]
    [InlineData("AddVehicle", "Manager", true)]
    [InlineData("AddMaintenance", "Driver", false)]
    [InlineData("AddMaintenance", "CustomerService", false)]
    [InlineData("AddMaintenance", "Maintenance", false)]
    [InlineData("AddMaintenance", "Manager", true)]
    [InlineData("Assign", "Driver", false)]
    [InlineData("Assign", "CustomerService", false)]
    [InlineData("Assign", "Manager", true)]
    [InlineData("ReviewLicense", "Driver", false)]
    [InlineData("ReviewLicense", "CustomerService", true)]
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
    [InlineData("Policies", "CustomerService", false)]
    [InlineData("Policies", "Manager", true)]
    public async Task Operations_enforce_capability_matrix(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<OperationsController>(action, Principal(role, 7)));

    [Theory]
    [InlineData("CreateShift", "Manager", true)]
    [InlineData("UpdateShift", "Driver", false)]
    [InlineData("CreateShift", "CustomerService", false)]
    [InlineData("CreateReport", "Driver", true)]
    [InlineData("CreateReport", "Customer", false)]
    [InlineData("AssignMaintenance", "Manager", true)]
    [InlineData("AssignMaintenance", "Maintenance", false)]
    [InlineData("ReplaceVehicle", "Manager", true)]
    [InlineData("ReplaceVehicle", "Driver", false)]
    [InlineData("InspectReplacement", "Maintenance", true)]
    public async Task Dispatch_endpoints_enforce_roles(string action, string role, bool allowed) =>
        Assert.Equal(allowed, await AuthorizeEndpoint<DispatchController>(action, Principal(role, 7)));

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
        foreach (var method in new[] { "Refund", "CreateInvoice", "Invoices", "Invoice", "Payments" })
            Assert.Equal(allowed || (role == "Customer" && method is "Invoices" or "Invoice" or "Payments"),
                await AuthorizeEndpoint<PaymentsController>(method, Principal(role)));
    }

    [Theory]
    [InlineData("Staff", false)]
    [InlineData("Driver", false)]
    [InlineData("CustomerService", false)]
    [InlineData("Manager", false)]
    [InlineData("Maintenance", true)]
    [InlineData("Admin", true)]
    public async Task Inspection_requires_technical_role_and_staff_identity(string role, bool allowed)
    {
        Assert.Equal(allowed, await AuthorizeEndpoint<OperationsController>("Pre", Principal(role, 7)));
        Assert.False(await AuthorizeEndpoint<OperationsController>("Pre", Principal(role)));
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
        var controller = Controller(Principal("Maintenance", 7), out var calls);
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
        Assert.IsType<ForbidResult>(await controller.DriverSchedule(8, from, from.AddDays(1), default));
        Assert.Empty(calls.Calls);
        await controller.DriverSchedule(null, from, from.AddDays(1), default);
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

    private static OperationsController Controller(ClaimsPrincipal principal, out RepositorySpy spy)
    {
        var store = DispatchProxy.Create<IOperationsRepository, RepositorySpy>();
        spy = (RepositorySpy)store;
        return new(new Car_Rental_System_BE.Application.Services.OperationsService(store)) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = principal } } };
    }

    public class RepositorySpy : DispatchProxy
    {
        public List<(string Method, object?[] Arguments)> Calls { get; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add((targetMethod!.Name, args!));
            return targetMethod.Name switch
            {
                "AddInspectionAsync" => Task.FromResult(new Inspection(1, 1, "pre_rental", true, null, null, null, null, null)),
                "ListDriverScheduleAsync" => Task.FromResult<IReadOnlyList<DriverScheduleEntry>>([]),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
        }
    }
}
