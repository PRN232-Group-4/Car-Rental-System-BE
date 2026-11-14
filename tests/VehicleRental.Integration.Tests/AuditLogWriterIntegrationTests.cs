using System.Net;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Car_Rental_System_BE.Infrastructure.Features.Identity;
using Car_Rental_System_BE.Infrastructure.Features.Audit;

namespace VehicleRental.Tests;

public sealed class AuditLogWriterIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Account_mutation_persists_request_IP_with_a_successful_audit_entry()
    {
        var actor = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,fullname) VALUES('ip-admin@test.local','IP Administrator') RETURNING id"))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name='Admin'", actor);
        var target = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,fullname) VALUES('ip-target@test.local','IP Target') RETURNING id"))!;
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("14.238.102.55");
        var services = new ServiceCollection();
        services.AddSingleton(db.DataSource);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext });
        services.AddScoped<IAuditRequestContext, HttpAuditRequestContext>();
        services.AddScoped<NpgsqlUserAdministrationRepository>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<NpgsqlUserAdministrationRepository>();
        await repository.SetStatusAsync(target, false, "Account review", actor, default);
        var item = Assert.Single((await new NpgsqlAuditLogRepository(db.DataSource).ListAsync(new() { Search = "ip-target@", Action = "account_locked" }, default)).Items);
        Assert.Equal("14.238.102.55", item.IpAddress);
        Assert.Equal("success", item.Status);
        Assert.Equal("Account review", item.Details);
        Assert.Equal(actor, item.ActorId);
        Assert.Equal(target, item.TargetUserId);
    }
}
