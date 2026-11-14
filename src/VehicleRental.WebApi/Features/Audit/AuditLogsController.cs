using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Car_Rental_System_BE.Application.Features.Audit;

namespace Car_Rental_System_BE.WebApi.Features.Audit;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = AuthorizationPolicies.Management)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class AuditLogsController(IAuditLogService auditLogs) : ControllerBase
{
    /// <summary>Danh mục nhật ký kiểm toán cho Admin/Manager. Mặc định page=1/pageSize=20; pageSize phải từ 1–100.</summary>
    [OperationDescription("Management policy: Admin or Manager only. Read-only account-administration audit directory. search matches actor/target email, full name, user IDs or audit ID (AUD-{id}); action filters public action names such as role_assigned, role_removed, account_locked, account_unlocked and session_revoked. Legacy stored actions are normalized; unknown actions return an empty page. timestamp is UTC; ordering is timestamp DESC then audit ID DESC. details is the persisted reason; missing historical IP or user metadata remains null. status describes the persisted event; historical successful mutations are success.")]
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Audit logs retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<AuditLogPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AuditLogQuery query, CancellationToken ct)
    {
        try { return Ok(await auditLogs.ListAsync(query, ct)); }
        catch (ArgumentException error) { return Problem(error.Message, statusCode: StatusCodes.Status400BadRequest); }
    }
}
