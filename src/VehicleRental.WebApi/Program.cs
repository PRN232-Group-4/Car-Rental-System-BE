using Car_Rental_System_BE.Infrastructure;
using Car_Rental_System_BE.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddWebApiLogging();

// Registration order is part of the contract: the last registration of a service type wins and
// IEnumerable<T> resolves in registration order. AddWebApi registers the options tree and MVC first,
// infrastructure after it, application services after that, and the auth/rate-limit wiring last because
// its JWT events resolve the application services registered above.
builder.Services.AddWebApi(builder.Configuration);
// Infrastructure owns the Npgsql data source, the Redis connection, hosted services
// and every repository/adapter registration. Program.cs only composes it.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddWebApiSecurity(builder.Configuration);

var app = builder.Build();

app.UseWebApi();

app.Run();

/// <summary>
/// Exposes the implicit entry-point type to <c>WebApplicationFactory&lt;Program&gt;</c> so the composition
/// smoke test can boot this exact host. The type carries no members.
/// </summary>
public partial class Program { }
