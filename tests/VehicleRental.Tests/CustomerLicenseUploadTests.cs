using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Registration;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.Application.Validators;
using Car_Rental_System_BE.Infrastructure.Services.Media;
using Car_Rental_System_BE.Domain.Interfaces;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

public sealed class CustomerLicenseUploadTests : IAsyncLifetime
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/a9sAAAAASUVORK5CYII=");
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly ImageStorageSpy _images = new();
    private LicenseRepositorySpy _repository = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new AssemblyPart(typeof(OperationsController).Assembly));
            });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.OperationFilter<ApiSuccessResponseOperationFilter>());
        var repository = DispatchProxy.Create<IOperationsRepository, LicenseRepositorySpy>();
        _repository = (LicenseRepositorySpy)repository;
        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton<IImageStorage>(_images);
        builder.Services.AddSingleton(DispatchProxy.Create<IUserRepository, UnusedUserRepository>());
        builder.Services.AddSingleton<IOperationsService, OperationsService>();
        builder.Services.AddSingleton<IMediaService, MediaService>();
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseSwagger();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
        _client.DefaultRequestHeaders.Add("X-Test-Role", "Customer");
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Unsubmitted_customer_is_none_without_mutating_the_pending_database_default(string? number)
    {
        _repository.License = new(number, null, null, null, null, "pending");
        using var response = await _client.GetAsync("/api/customer/license");
        using var body = await Envelope(response, 200, true);
        Assert.Equal("none", body.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("pending", _repository.License.Status);
        Assert.Equal(0, _repository.Saves);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("approved")]
    [InlineData("rejected")]
    public async Task Submitted_license_keeps_its_review_status(string status)
    {
        _repository.License = _repository.License with { Status = status };
        using var response = await _client.GetAsync("/api/customer/license");
        using var body = await Envelope(response, 200, true);
        Assert.Equal(status, body.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task First_multipart_submission_from_an_unsubmitted_customer_is_allowed()
    {
        _repository.License = new(null, null, null, null, null, "pending");
        using var form = LicenseForm();
        using var response = await _client.PutAsync("/api/customer/license", form);
        using var body = await Envelope(response, 200, true);
        Assert.Equal("pending", body.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(1, _repository.Saves);
        Assert.Equal(Png, _images.Bytes);
    }

    [Fact]
    public async Task Missing_customer_still_returns_null()
    {
        _repository.Exists = false;
        using var response = await _client.GetAsync("/api/customer/license");
        using var body = await Envelope(response, 200, true);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task Swagger_documents_the_unsubmitted_license_state()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = body.RootElement.GetProperty("paths").GetProperty("/api/customer/license").GetProperty("get");
        Assert.Contains("status none", operation.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Multipart_upload_persists_cloudinary_url_and_resubmits_approved_license()
    {
        using var form = LicenseForm();
        using var response = await _client.PutAsync("/api/customer/license", form);
        using var body = await Envelope(response, 200, true);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("https://res.cloudinary.com/test/license.png", data.GetProperty("imageUrl").GetString());
        Assert.Equal("pending", data.GetProperty("status").GetString());
        Assert.Equal("ABC123", data.GetProperty("number").GetString());
        Assert.Equal("Driver license saved successfully.", body.RootElement.GetProperty("message").GetString());
        Assert.Equal("customer-licenses", _images.Folder);
        Assert.Equal(Png, _images.Bytes);
        Assert.Equal(1, _repository.Saves);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issue-after-expiry")]
    [InlineData("missing-expiry")]
    [InlineData("missing-issue")]
    [InlineData("missing-file")]
    [InlineData("empty-file")]
    [InlineData("invalid-content")]
    [InlineData("wrong-mime")]
    [InlineData("oversize")]
    [InlineData("blank-number")]
    [InlineData("missing-type")]
    public async Task Invalid_form_never_uploads_or_persists(string invalid)
    {
        using var form = LicenseForm(invalid);
        using var response = await _client.PutAsync("/api/customer/license", form);
        using var body = await Envelope(response, 400, false);
        Assert.Equal(0, _images.Uploads);
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public async Task Failed_cloudinary_upload_does_not_replace_existing_license()
    {
        _images.Fail = true;
        using var form = LicenseForm();
        using var response = await _client.PutAsync("/api/customer/license", form);
        using var body = await Envelope(response, 500, false);
        Assert.Equal(0, _repository.Saves);
        Assert.Equal("approved", _repository.License.Status);
        Assert.DoesNotContain("provider secret", body.RootElement.GetRawText());
        Assert.Equal("An unexpected server error occurred.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Missing_cloudinary_configuration_is_a_technical_upload_failure()
    {
        using var content = new MemoryStream(Png);
        var adapter = new CloudinaryImageStorage(Options.Create(new CloudinaryOptions()));
        var exception = await Assert.ThrowsAsync<ImageStorageException>(() => adapter.UploadAsync(content, "proof.png", "customer-licenses", default));
        Assert.Equal("Image storage is not configured.", exception.Message);
    }

    [Fact]
    public async Task Missing_customer_does_not_upload_an_orphaned_image()
    {
        _repository.Exists = false;
        using var form = LicenseForm();
        using var response = await _client.PutAsync("/api/customer/license", form);
        using var body = await Envelope(response, 404, false);
        Assert.Equal(0, _images.Uploads);
        Assert.Equal(0, _repository.Saves);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Manager", 403)]
    [InlineData("Driver", 403)]
    public async Task Only_customer_can_submit_their_license(string? role, int status)
    {
        _client.DefaultRequestHeaders.Remove("X-Test-Role");
        if (role is not null) _client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var form = LicenseForm();
        using var response = await _client.PutAsync("/api/customer/license", form);
        using var body = await Envelope(response, status, false);
        Assert.Equal(0, _images.Uploads);
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public async Task Existing_json_url_contract_is_available_on_explicit_url_route()
    {
        var json = JsonSerializer.Serialize(new { number = "ABC123", type = "B", issueDate = DateOnly.FromDateTime(DateTime.UtcNow), expiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), imageUrl = "https://example.test/existing.png" });
        using var response = await _client.PutAsync("/api/customer/license/url", new StringContent(json, Encoding.UTF8, "application/json"));
        using var body = await Envelope(response, 200, true);
        Assert.Equal("https://example.test/existing.png", body.RootElement.GetProperty("data").GetProperty("imageUrl").GetString());
        Assert.Equal("pending", _repository.License.Status);
        Assert.Equal(0, _images.Uploads);
    }

    [Theory]
    [InlineData("number")]
    [InlineData("type")]
    [InlineData("issueDate")]
    [InlineData("expiryDate")]
    [InlineData("imageUrl")]
    public async Task Url_submission_requires_every_field(string missing)
    {
        var payload = new Dictionary<string, object?>
        {
            ["number"] = "ABC123", ["type"] = "B",
            ["issueDate"] = DateOnly.FromDateTime(DateTime.UtcNow),
            ["expiryDate"] = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
            ["imageUrl"] = "https://example.test/existing.png"
        };
        payload.Remove(missing);
        using var response = await _client.PutAsync("/api/customer/license/url",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        using var body = await Envelope(response, 400, false);
        Assert.Contains(body.RootElement.GetProperty("error").GetProperty("errors").EnumerateObject(),
            field => field.Name.Equals(missing, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, _images.Uploads);
        Assert.Equal(0, _repository.Saves);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid-url")]
    public void Application_rejects_missing_or_invalid_image(string? imageUrl)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Throws<ArgumentException>(() => CustomerLicenseValidator.Validate(
            new CustomerLicense("ABC123", "B", today, today.AddYears(1), imageUrl, "pending")));
    }

    [Fact]
    public void Application_rejects_missing_issue_date()
    {
        Assert.Throws<ArgumentException>(() => CustomerLicenseValidator.Validate(
            new CustomerLicense("ABC123", "B", null, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), "https://example.test/license.png", "pending")));
    }

    [Theory]
    [InlineData("Customer", "/api/media/staff-license", false)]
    [InlineData("Manager", "/api/media/staff-license", false)]
    [InlineData("Admin", "/api/media/staff-license", true)]
    [InlineData("Maintenance", "/api/media/inspection", true)]
    [InlineData("Driver", "/api/media/inspection", false)]
    [InlineData("Driver", "/api/media/report", true)]
    [InlineData("Customer", "/api/media/report", false)]
    public async Task Additional_evidence_uploads_use_resource_creation_roles(string role, string path, bool allowed)
    {
        _client.DefaultRequestHeaders.Remove("X-Test-Role");
        _client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png); file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "proof.png");
        using var response = await _client.PostAsync(path, form);
        using var body = await Envelope(response, allowed ? 200 : 403, allowed);
        Assert.Equal(allowed ? 1 : 0, _images.Uploads);
    }

    [Fact]
    public async Task Report_upload_requires_staff_profile_even_with_staff_role()
    {
        _client.DefaultRequestHeaders.Remove("X-Test-Role");
        _client.DefaultRequestHeaders.Add("X-Test-Role", "Driver");
        _client.DefaultRequestHeaders.Add("X-Test-No-Staff", "true");
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png); file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "proof.png");
        using var response = await _client.PostAsync("/api/media/report", form);
        using var body = await Envelope(response, 403, false);
        Assert.Equal(0, _images.Uploads);
    }

    [Fact]
    public async Task Swagger_exposes_binary_file_input_and_envelope_response()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = json.RootElement.GetProperty("paths").GetProperty("/api/customer/license").GetProperty("put");
        var schema = operation.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
            schema = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/').Last());
        var file = schema.GetProperty("properties").EnumerateObject().Single(p => p.Name.Equals("file", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.Equal("binary", file.GetProperty("format").GetString());
        var required = schema.GetProperty("required").EnumerateArray().Select(field => field.GetString()!.ToLowerInvariant()).ToArray();
        Assert.All(new[] { "number", "type", "issuedate", "expirydate", "file" }, field => Assert.Contains(field, required));
        var envelope = operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal(6, envelope.GetProperty("required").GetArrayLength());
        Assert.Equal("boolean", envelope.GetProperty("properties").GetProperty("is_success").GetProperty("type").GetString());
    }

    private static MultipartFormDataContent LicenseForm(string? invalid = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(invalid == "blank-number" ? " " : " ABC123 "), "number");
        if (invalid != "missing-type") form.Add(new StringContent("B"), "type");
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        if (invalid != "missing-expiry") form.Add(new StringContent((invalid == "expired" ? expiry.AddYears(-2) : expiry).ToString("yyyy-MM-dd")), "expiryDate");
        if (invalid != "missing-issue") form.Add(new StringContent((invalid == "issue-after-expiry" ? expiry.AddDays(1) : DateOnly.FromDateTime(DateTime.UtcNow)).ToString("yyyy-MM-dd")), "issueDate");
        if (invalid != "missing-file")
        {
            var bytes = invalid switch { "invalid-content" => Encoding.UTF8.GetBytes("not an image"), "empty-file" => [], "oversize" => new byte[5 * 1024 * 1024 + 1], _ => Png };
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(invalid == "wrong-mime" ? "text/plain" : "image/png");
            form.Add(file, "file", "license.png");
        }
        return form;
    }

    private static async Task<JsonDocument> Envelope(HttpResponseMessage response, int code, bool success)
    {
        Assert.Equal(code, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(6, json.RootElement.EnumerateObject().Count());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(success, json.RootElement.GetProperty("is_success").GetBoolean());
        Assert.Equal(success ? "success" : "error", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty(success ? "error" : "data").ValueKind);
        return json;
    }

    public class LicenseRepositorySpy : DispatchProxy
    {
        public int Saves { get; private set; }
        public bool Exists { get; set; } = true;
        public CustomerLicense License { get; set; } = new("OLD", "B", null, null, "https://example.test/old.png", "approved");
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "GetLicenseAsync") return Task.FromResult<CustomerLicense?>(Exists ? License : null);
            if (method.Name != "SaveLicenseAsync") throw new NotSupportedException(method.Name);
            Assert.Equal("customer@test.local", args![0]);
            Saves++; License = (CustomerLicense)args[1]!;
            return Task.FromResult(License);
        }
    }

    public class UnusedUserRepository : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException(method!.Name);
    }

    private sealed class ImageStorageSpy : IImageStorage
    {
        public int Uploads { get; private set; }
        public bool Fail { get; set; }
        public string? Folder { get; private set; }
        public byte[]? Bytes { get; private set; }
        public async Task<StoredImage> UploadAsync(Stream content, string fileName, string folder, CancellationToken ct)
        {
            Uploads++; Folder = folder;
            if (Fail) throw new ImageStorageException("provider secret");
            using var buffer = new MemoryStream(); await content.CopyToAsync(buffer, ct); Bytes = buffer.ToArray();
            return new("https://res.cloudinary.com/test/license.png", "proof-id");
        }
    }

    public sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString()),
                new Claim(ClaimTypes.Email, "customer@test.local")], "test");
            if (!Request.Headers.ContainsKey("X-Test-No-Staff")) identity.AddClaim(new Claim("staff_id", "1"));
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "test")));
        }
    }
}
