using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.WebApi.Common.Authentication;

public sealed class HttpAuditRequestContext(IHttpContextAccessor accessor) : IAuditRequestContext
{
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
