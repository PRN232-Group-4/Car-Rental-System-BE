using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/admin/navigation-counts")]
[Authorize(Roles = "Admin,Manager,CustomerService,Maintenance,Staff")]
public sealed class NavigationCountsController(INavigationCountsService service) : ControllerBase
{
    /// <summary>Đếm các hàng đợi vận hành đang chờ mà bên gọi thấy được; hàng đợi không có quyền truy cập trả về 0.</summary>
    [OperationDescription("Admin/Manager/CustomerService/Maintenance/Staff only. A scalar snapshot (no pagination): rentals=pending; inspections=pre_inspection/post_inspection without matching current-vehicle evidence; maintenance=open/scheduled; reports=open staff reports; licenses=submitted pending licenses. Counts match list scope: reports are global for Admin/Manager and restricted to the authenticated staff_id for other staff. Inaccessible queues are zero; Staff only sees own reports. New means awaiting processing, not unread per user.")]
    [SuccessMessage("Pending navigation queue counts retrieved successfully.")]
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
