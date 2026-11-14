using Car_Rental_System_BE.WebApi.Common.Responses;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Car_Rental_System_BE.Application.Features.Identity;

namespace VehicleRental.Tests;

/// <summary>Exercises the shared MVC/middleware contract over HTTP without DB/Redis/provider dependencies.</summary>
public sealed class ApiResponseTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddAuthorization();
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.AssemblyPart(typeof(ApiContractController).Assembly));
            });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.OperationFilter<ApiSuccessResponseOperationFilter>();
            options.SchemaFilter<PasswordRequestSchemaFilter>();
        });
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseSwagger();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.MapGet("/__minimal/health", () => Results.Ok(ApiResponse.Create(new { status = "Healthy" }, "Service is healthy.")));
        _app.MapGet("/__minimal/unhealthy", () => Results.Json(ApiResponse.Failure(503, message: "Database is unavailable."), statusCode: 503));
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("/__response/typed", 200)]
    [InlineData("/__response/created", 201)]
    [InlineData("/__response/accepted", 202)]
    [InlineData("/__response/no-content", 200)]
    [InlineData("/__response/status-created", 200)]
    [InlineData("/__response/json", 200)]
    [InlineData("/__response/wrapped", 200)]
    [InlineData("/__minimal/health", 200)]
    public async Task Successes_have_six_fields_and_a_boolean(string path, int code)
    {
        using var response = await _client.GetAsync(path);
        using var body = await AssertEnvelope(response, code, true);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("error").ValueKind);
        if (path.EndsWith("no-content") || path.EndsWith("status-created") || path.EndsWith("accepted"))
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
        if (path.EndsWith("wrapped"))
            Assert.Equal(7, body.RootElement.GetProperty("data").GetProperty("id").GetInt32());
    }

    [Theory]
    [InlineData("/__response/problem", 409)]
    [InlineData("/__response/validation", 400)]
    [InlineData("/__response/unauthorized", 401)]
    [InlineData("/__response/forbidden", 403)]
    [InlineData("/__response/secure", 401)]
    [InlineData("/__response/missing", 404)]
    [InlineData("/__response/argument", 400)]
    [InlineData("/__response/access-denied", 403)]
    [InlineData("/__response/unexpected", 500)]
    [InlineData("/__minimal/unhealthy", 503)]
    [InlineData("/__not-a-route", 404)]
    public async Task Errors_have_six_fields_and_a_boolean(string path, int code)
    {
        using var response = await _client.GetAsync(path);
        using var body = await AssertEnvelope(response, code, false);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
        Assert.Equal(code, body.RootElement.GetProperty("error").GetProperty("status").GetInt32());
        if (code == 500)
        {
            Assert.Equal("An unexpected server error occurred.", body.RootElement.GetProperty("message").GetString());
            Assert.DoesNotContain("private exception", body.RootElement.GetRawText());
        }
        if (path.EndsWith("validation"))
            Assert.True(body.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("email", out _));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{")]
    public async Task Automatic_validation_and_malformed_json_are_normalized(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/__response/input", content);
        using var body = await AssertEnvelope(response, 400, false);
        Assert.NotEmpty(body.RootElement.GetProperty("error").GetProperty("errors").EnumerateObject());
    }

    [Fact]
    public async Task Method_not_allowed_is_normalized_and_keeps_allow_header()
    {
        using var response = await _client.PostAsync("/__response/typed", null);
        using var body = await AssertEnvelope(response, 405, false);
        Assert.Contains("GET", response.Content.Headers.Allow);
    }

    [Theory]
    [InlineData("register-input", 5, 400)]
    [InlineData("register-input", 6, 200)]
    [InlineData("register-input", 20, 200)]
    [InlineData("register-input", 21, 400)]
    [InlineData("login-input", 5, 400)]
    [InlineData("login-input", 6, 200)]
    [InlineData("login-input", 20, 200)]
    [InlineData("login-input", 21, 400)]
    public async Task Password_length_is_enforced_by_MVC_and_retains_response_contract(string endpoint, int length, int code)
    {
        var json = JsonSerializer.Serialize(new { email = "customer@example.test", fullName = "Customer", password = new string('a', length) });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync($"/__response/{endpoint}", content);
        using var body = await AssertEnvelope(response, code, code == 200);
        if (code == 400)
        {
            var errors = body.RootElement.GetProperty("error").GetProperty("errors");
            Assert.Contains(errors.EnumerateObject(), error =>
                error.Name.Equals("password", StringComparison.OrdinalIgnoreCase)
                && error.Value[0].GetString() == "Mật khẩu phải có từ 6 đến 20 ký tự.");
        }
    }

    [Fact]
    public async Task Swagger_documents_password_minimum_and_maximum_length()
    {
        using var body = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var schemas = body.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "StartRegistrationRequest", "LoginRequest" })
        {
            var password = schemas.GetProperty(name).GetProperty("properties").GetProperty("password");
            Assert.Equal(6, password.GetProperty("minLength").GetInt32());
            Assert.Equal(20, password.GetProperty("maxLength").GetInt32());
        }
    }

    [Fact]
    public async Task Redirect_preserves_location_and_is_not_replaced_with_json()
    {
        using var response = await _client.GetAsync("/__response/redirect");
        Assert.Equal(302, (int)response.StatusCode);
        Assert.Equal("/destination", response.Headers.Location!.OriginalString);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Swagger_describes_success_error_auth_and_no_content_envelopes()
    {
        using var body = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = body.RootElement.GetProperty("paths");
        foreach (var (path, code) in new[] { ("/__response/created", "201"), ("/__response/no-content", "200"), ("/__response/problem", "409"), ("/__response/secure", "401"), ("/__response/secure", "403") })
        {
            var schema = paths.GetProperty(path).GetProperty("get").GetProperty("responses").GetProperty(code)
                .GetProperty("content").GetProperty("application/json").GetProperty("schema");
            Assert.Equal(6, schema.GetProperty("required").GetArrayLength());
            Assert.Equal(6, schema.GetProperty("properties").EnumerateObject().Count());
            Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("is_success").GetProperty("type").GetString());
            Assert.Equal("integer", schema.GetProperty("properties").GetProperty("code").GetProperty("type").GetString());
        }
        Assert.False(paths.GetProperty("/__response/no-content").GetProperty("get").GetProperty("responses").TryGetProperty("204", out _));
    }

    [Fact]
    public void Null_fields_are_serialized_even_when_serializer_ignores_nulls()
    {
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        foreach (var value in new[] { ApiResponse.Create(null, "Done."), ApiResponse.Failure(400) })
        {
            using var body = JsonDocument.Parse(JsonSerializer.Serialize(value, options));
            Assert.Equal(6, body.RootElement.EnumerateObject().Count());
            Assert.True(body.RootElement.TryGetProperty("data", out _));
            Assert.True(body.RootElement.TryGetProperty("error", out _));
            Assert.Equal(value.IsSuccess ? JsonValueKind.True : JsonValueKind.False, body.RootElement.GetProperty("is_success").ValueKind);
        }
    }

    private static async Task<JsonDocument> AssertEnvelope(HttpResponseMessage response, int code, bool successful)
    {
        Assert.Equal(code, (int)response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, root.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(code, root.GetProperty("code").GetInt32());
        Assert.Equal(successful ? "success" : "error", root.GetProperty("status").GetString());
        Assert.Equal(successful ? JsonValueKind.True : JsonValueKind.False, root.GetProperty("is_success").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
        return body;
    }
}

[ApiController]
[Route("__response")]
public sealed class ApiContractController : ControllerBase
{
    [HttpGet("typed")] public Task<ContractData> Typed() => Task.FromResult(new ContractData(1));
    [HttpGet("created"), ProducesResponseType<ContractData>(201)] public IActionResult CreatedData() => Created("/destination", new ContractData(1));
    [HttpGet("accepted")] public IActionResult AcceptedData() => Accepted();
    [HttpGet("no-content"), ProducesResponseType(204)] public IActionResult NoContentData() => NoContent();
    [HttpGet("status-created")] public IActionResult StatusCreated() => StatusCode(201);
    [HttpGet("json")] public IActionResult JsonData() => new JsonResult(new ContractData(1));
    [HttpGet("wrapped")] public IActionResult Wrapped() => Ok(ApiResponse.Create(new ContractData(7), "Already wrapped."));
    [HttpGet("problem"), ProducesResponseType<ProblemDetails>(409)] public IActionResult ConflictData() => Problem("Already paid.", statusCode: 409);
    [HttpGet("validation")] public IActionResult Validation()
    {
        ModelState.AddModelError("email", "Invalid email.");
        return ValidationProblem(ModelState);
    }
    [HttpPost("input")] public IActionResult Input(ContractInput input) => Ok(input);
    [HttpPost("register-input")] public IActionResult RegistrationInput(StartRegistrationRequest input) => Ok(new { accepted = true });
    [HttpPost("login-input")] public IActionResult LoginInput(LoginRequest input) => Ok(new { accepted = true });
    [HttpGet("unauthorized")] public IActionResult UnauthorizedData() => Unauthorized();
    [HttpGet("forbidden")] public IActionResult ForbiddenData() => Forbid();
    [HttpGet("secure"), Authorize] public IActionResult Secure() => Ok();
    [HttpGet("missing")] public IActionResult Missing() => throw new KeyNotFoundException("Missing rental.");
    [HttpGet("argument")] public IActionResult Argument() => throw new ArgumentException("Invalid schedule.");
    [HttpGet("access-denied")] public IActionResult AccessDenied() => throw new UnauthorizedAccessException("Outside scope.");
    [HttpGet("unexpected")] public IActionResult Unexpected() => throw new Exception("private exception");
    [HttpGet("redirect")] public IActionResult RedirectData() => Redirect("/destination");
}

public sealed record ContractData(int Id);
public sealed record ContractInput([param: Required] string Email);
