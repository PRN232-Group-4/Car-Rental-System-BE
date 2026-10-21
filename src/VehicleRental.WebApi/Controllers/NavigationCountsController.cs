using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/admin/navigation-counts")]
[Authorize(Roles = "Admin,Manager,CustomerService,Maintenance,Staff")]
public sealed class NavigationCountsController(INavigationCountsService service) : ControllerBase
{
    /// <summary>Counts pending operational queues visible to the caller; inaccessible queues return zero.</summary>
    [HttpGet]
    [ProducesResponseType<NavigationCounts>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<NavigationCounts> Get(CancellationToken ct) => service.GetAsync(new(
        AuthorizationPolicies.CanServeCustomers(User),
        AuthorizationPolicies.CanManage(User) || User.IsInRole("Maintenance"),
        AuthorizationPolicies.CanServeCustomers(User) || User.IsInRole("Maintenance"),
        AuthorizationPolicies.CanManage(User), AuthorizationPolicies.StaffId(User)), ct);
}
