using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.DTOs.Organization;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Services.Authentication;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RefreshAuthorizationTests
{
    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(-1, false)]
    public async Task Refresh_preserves_version_and_rejects_changed_or_disabled_account(int currentVersion, bool allowed)
    {
        // Exercise the real token store and controller; the Redis protocol is replaced with in-memory commands.
        var database = DispatchProxy.Create<IDatabase, RedisMemory>();
        var multiplexer = DispatchProxy.Create<IConnectionMultiplexer, RedisConnection>();
        ((RedisConnection)multiplexer).Database = database;
        var store = new RedisRefreshTokenStore(multiplexer, Options.Create(new TokenOptions()));
        var issued = await store.CreateAsync("staff@test.local", false, default, 4);
        var accounts = DispatchProxy.Create<IAccountRepository, Accounts>();
        ((Accounts)accounts).Account = currentVersion < 0 ? null : new(1, "staff@test.local", "Staff", null, null, null, null,
            ["Staff", "CustomerService"], AuthorizationVersion: currentVersion);
        var controller = new SessionController(new SessionService(accounts, store,
            new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('x', 64) }), Options.Create(new TokenOptions()))))
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Headers.Cookie = $"refresh_token={issued.Token}";
        var result = await controller.Refresh(default);
        Assert.False(await store.IsActiveAsync(issued.SessionId, "staff@test.local", default));
        if (allowed)
        {
            Assert.IsType<NoContentResult>(result);
            Assert.Single(((RedisMemory)database).Values);
            Assert.Contains("\"AuthorizationVersion\":4", ((RedisMemory)database).Values.Values.Single().ToString());
            Assert.Contains(controller.Response.Headers.SetCookie, cookie => cookie!.StartsWith("access_token=") && cookie.Contains("httponly"));
        }
        else
        {
            Assert.IsType<UnauthorizedResult>(result);
            Assert.Empty(((RedisMemory)database).Values);
        }
        Assert.Null(await store.RotateAsync(issued.Token, default));
    }

    public class Accounts : DispatchProxy
    {
        public Account? Account { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Task.FromResult(Account);
    }

    public class RedisConnection : DispatchProxy
    {
        public IDatabase Database { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == "GetDatabase" ? Database : throw new NotSupportedException(method.Name);
    }

    public class RedisMemory : DispatchProxy
    {
        public Dictionary<string, RedisValue> Values { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var key = args![0]!.ToString()!;
            switch (method!.Name)
            {
                case "StringSetAsync":
                    Values[key] = (RedisValue)args[1]!;
                    return Task.FromResult(true);
                case "StringGetAsync":
                    return Task.FromResult(Values.GetValueOrDefault(key, RedisValue.Null));
                case "StringGetDeleteAsync":
                    Values.Remove(key, out var value);
                    return Task.FromResult(value);
                case "SetAddAsync":
                case "SetRemoveAsync":
                    return Task.FromResult(true);
                default: throw new NotSupportedException(method.Name);
            }
        }
    }
}
