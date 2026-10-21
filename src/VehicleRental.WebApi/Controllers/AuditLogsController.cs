using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = AuthorizationPolicies.Management)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class AuditLogsController(IAuditLogService auditLogs) : ControllerBase
{
    /// <summary>Admin/Manager audit directory. Defaults page=1/pageSize=20; pageSize must be 1–100.</summary>
    [HttpGet]
    [ProducesResponseType<AuditLogPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AuditLogQuery query, CancellationToken ct)
    {
        try { return Ok(await auditLogs.ListAsync(query, ct)); }
        catch (ArgumentException error) { return Problem(error.Message, statusCode: StatusCodes.Status400BadRequest); }
    }
}
