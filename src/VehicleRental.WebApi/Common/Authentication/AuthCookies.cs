using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Common.Authentication;

public static class AuthCookies
{
    public const string AccessTokenName = "access_token";
    public const string RefreshTokenName = "refresh_token";

    public static void Write(HttpRequest request, HttpResponse response, string accessToken, string refreshToken, DateTimeOffset refreshExpiresUtc, TimeSpan accessTokenLifetime)
    {
        var sameSite = ResolveSameSite(request);
        response.Cookies.Append(AccessTokenName, accessToken, new CookieOptions { HttpOnly = true, Secure = true, SameSite = sameSite, Path = "/", Expires = DateTimeOffset.UtcNow.Add(accessTokenLifetime) });
        response.Cookies.Append(RefreshTokenName, refreshToken, new CookieOptions { HttpOnly = true, Secure = true, SameSite = sameSite, Path = "/api/auth", Expires = refreshExpiresUtc });
    }

    public static void Clear(HttpRequest request, HttpResponse response)
    {
        var sameSite = ResolveSameSite(request);
        response.Cookies.Delete(AccessTokenName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = sameSite, Path = "/" });
        response.Cookies.Delete(RefreshTokenName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = sameSite, Path = "/api/auth" });
    }

    /// <summary>
    /// Request cross-site từ trình duyệt (fetch/XHR) luôn có header `Origin`; khi đó
    /// cookie phiên phải là `SameSite=None` (kèm `Secure`) thì trình duyệt mới chấp nhận
    /// `Set-Cookie` trả về từ API ở domain khác. Native/server-to-server không gửi
    /// `Origin` nên giữ `SameSite=Lax`.
    /// </summary>
    private static SameSiteMode ResolveSameSite(HttpRequest request)
        => request.Headers.ContainsKey("Origin") ? SameSiteMode.None : SameSiteMode.Lax;
}
