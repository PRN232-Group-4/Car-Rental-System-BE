using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Middlewares.Authentication;

public static class AuthCookies
{
    public const string AccessTokenName = "access_token";
    public const string RefreshTokenName = "refresh_token";

    public static void Write(HttpResponse response, string accessToken, string refreshToken, DateTimeOffset refreshExpiresUtc, TimeSpan accessTokenLifetime)
    {
        response.Cookies.Append(AccessTokenName, accessToken, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = DateTimeOffset.UtcNow.Add(accessTokenLifetime) });
        response.Cookies.Append(RefreshTokenName, refreshToken, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/api/auth", Expires = refreshExpiresUtc });
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(AccessTokenName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/" });
        response.Cookies.Delete(RefreshTokenName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/api/auth" });
    }
}
