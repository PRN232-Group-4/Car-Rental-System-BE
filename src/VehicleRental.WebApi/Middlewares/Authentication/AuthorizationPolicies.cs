using Car_Rental_System_BE.Application.DTOs.Operations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Car_Rental_System_BE.Application.Features.Identity;

namespace Car_Rental_System_BE.WebApi.Middlewares.Authentication;

public static class AuthorizationPolicies
{
    public const string Admin = "RequireAdmin";
    public const string Customer = "RequireCustomer";
    public const string Management = "Management";
    public const string CustomerService = "CustomerService";
    public const string Inspection = "Inspection";
    public const string Maintenance = "Maintenance";
    public const string DriverSchedule = "DriverSchedule";
    public const string RentalRead = "RentalRead";
    public const string FinancialRead = "FinancialRead";

    public static void Configure(AuthorizationOptions options)
    {
        Add(Admin, "Admin");
        Add(Customer, "Customer");
        Add(Management, "Admin", "Manager");
        Add(CustomerService, "Admin", "Manager", "CustomerService");
        options.AddPolicy(Inspection, policy => policy.RequireAuthenticatedUser()
            .RequireRole("Admin", "Manager", "Maintenance").RequireAssertion(context => StaffId(context.User) is not null));
        Add(Maintenance, "Admin", "Manager", "Maintenance");
        Add(DriverSchedule, "Admin", "Manager", "Driver");
        Add(RentalRead, "Admin", "Manager", "CustomerService", "Maintenance", "Customer", "Driver");
        Add(FinancialRead, "Admin", "Manager", "CustomerService", "Customer");
        void Add(string name, params string[] roles) => options.AddPolicy(name,
            policy => policy.RequireAuthenticatedUser().RequireRole(roles));
    }

    public static bool CanManage(ClaimsPrincipal user) => user.IsInRole("Admin") || user.IsInRole("Manager");
    public static bool CanServeCustomers(ClaimsPrincipal user) => CanManage(user) || user.IsInRole("CustomerService");
    public static bool CanReadAllRentals(ClaimsPrincipal user) => CanServeCustomers(user) || user.IsInRole("Maintenance");
    public static int? StaffId(ClaimsPrincipal user) => int.TryParse(user.FindFirstValue("staff_id"), out var id) && id > 0 ? id : null;
    public static bool IsCurrentAccount(ClaimsPrincipal user, Account? account) => account is not null
        && user.FindFirstValue(ClaimTypes.NameIdentifier) == account.Id.ToString(CultureInfo.InvariantCulture)
        && user.FindFirstValue("authorization_version") == account.AuthorizationVersion.ToString(CultureInfo.InvariantCulture);
}
