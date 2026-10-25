using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.WebApi.Controllers;
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

public sealed class SignedRentalContractTests(PostgresFixture db) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF\n");
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private NpgsqlOperationsRepository Operations => new(db.DataSource);
    private NpgsqlRentalContractDocumentRepository Documents => new(db.DataSource);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, ContractAuthentication>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers(o => o.Filters.Add<ApiSuccessResponseFilter>()).ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            parts.ApplicationParts.Add(new AssemblyPart(typeof(RentalContractDocumentsController).Assembly));
        });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o => o.OperationFilter<ApiSuccessResponseOperationFilter>());
        builder.Services.AddSingleton<IOperationsService>(new OperationsService(Operations));
        builder.Services.AddSingleton<IRentalContractDocumentRepository>(Documents);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IRentalContractDocumentService, RentalContractDocumentService>();
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseSwagger();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    private async Task<(Rental Rental, string Email)> RentalAsync(string rentalType = "longterm")
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.AddDays(1);
        var rental = await Operations.CreateRentalAsync(email, vehicle, null, rentalType, start, start.AddDays(30), false, null, false, default);
        Authenticate(email);
        return (rental, email);
    }

    private void Authenticate(string email, string? role = "Customer")
    {
        _client.DefaultRequestHeaders.Clear();
        if (role is null) return;
        _client.DefaultRequestHeaders.Add("X-Test-Role", role);
        _client.DefaultRequestHeaders.Add("X-Test-Email", email);
    }

    private static MultipartFormDataContent Form(byte[]? bytes = null, string mime = "application/pdf", string name = "contract.pdf", string? signedAt = null, bool noFile = false, bool noDate = false)
    {
        var form = new MultipartFormDataContent();
        if (!noFile)
        {
            var file = new ByteArrayContent(bytes ?? Pdf);
            file.Headers.ContentType = new MediaTypeHeaderValue(mime);
            // RFC 5987 encodes quotes/path segments without HttpClient rejecting the
            // header before the request reaches the server's filename sanitization.
            file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "file", FileNameStar = name
            };
            form.Add(file);
        }
        if (!noDate) form.Add(new StringContent(signedAt ?? DateTime.UtcNow.AddMinutes(-1).ToString("O")), "signedAt");
        return form;
    }

    private async Task<JsonDocument> Upload(int id, MultipartFormDataContent form, int code = 200)
    {
        using (form)
        using (var response = await _client.PostAsync($"/api/rentals/{id}/contract/upload", form))
            return await Envelope(response, code);
    }

    private static async Task<JsonDocument> Envelope(HttpResponseMessage response, int code)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == code, $"Expected {code}, actual {(int)response.StatusCode}: {raw}");
        var json = JsonDocument.Parse(raw);
        Assert.Equal(6, json.RootElement.EnumerateObject().Count());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(code < 400, json.RootElement.GetProperty("is_success").GetBoolean());
        Assert.Equal(code < 400 ? "success" : "error", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty(code < 400 ? "error" : "data").ValueKind);
        return json;
    }

    [Fact]
    public async Task Owner_upload_download_roundtrip_and_atomic_replacement_before_start()
    {
        var (rental, _) = await RentalAsync();
        using var body = await Upload(rental.Id, Form(name: "../../bad\".pdf"));
        var data = body.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("hasUploadedContract").GetBoolean());
        Assert.Equal($"/api/rentals/{rental.Id}/contract/document", data.GetProperty("contractLink").GetString());
        Assert.Equal("Signed rental contract uploaded successfully.", body.RootElement.GetProperty("message").GetString());
        using var download = await _client.GetAsync($"/api/rentals/{rental.Id}/contract/document");
        Assert.Equal(200, (int)download.StatusCode);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal($"signed-contract-{rental.Id}.pdf", download.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Contains("no-store", download.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/a9sAAAAASUVORK5CYII=");
        using var replacement = await Upload(rental.Id, Form(png, "image/png", "signed.png"));
        Assert.Equal(png, (await Documents.GetAsync(rental.Id, default))!.Content);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM rental_contract_documents WHERE rental_id=$1", rental.Id));
        Assert.True((await Operations.GetRentalAsync(rental.Id, default))!.HasUploadedContract);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("CustomerService")]
    public async Task Operations_can_upload_and_download(string role)
    {
        var (rental, _) = await RentalAsync();
        Authenticate("operator@test.local", role);
        using var body = await Upload(rental.Id, Form());
        using var response = await _client.GetAsync($"/api/rentals/{rental.Id}/contract/document");
        Assert.Equal(200, (int)response.StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Driver", 403)]
    [InlineData("Maintenance", 403)]
    [InlineData("Staff", 403)]
    [InlineData("Customer", 404)]
    public async Task Anonymous_unrelated_roles_and_other_customer_cannot_access(string? role, int code)
    {
        var (rental, _) = await RentalAsync();
        using var upload = await Upload(rental.Id, Form());
        Authenticate("outsider@test.local", role);
        using var deniedUpload = await Upload(rental.Id, Form(), code);
        using var response = await _client.GetAsync($"/api/rentals/{rental.Id}/contract/document");
        using var deniedDownload = await Envelope(response, code);
        Assert.Equal(Pdf, (await Documents.GetAsync(rental.Id, default))!.Content);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("wrong-mime")]
    [InlineData("wrong-extension")]
    [InlineData("fake-pdf")]
    [InlineData("missing-file")]
    [InlineData("missing-date")]
    [InlineData("future-date")]
    [InlineData("invalid-date")]
    public async Task Rejects_invalid_file_and_date_without_persisting(string invalid)
    {
        var (rental, _) = await RentalAsync();
        using var body = await Upload(rental.Id, Form(
            invalid switch { "empty" => [], "oversize" => new byte[10 * 1024 * 1024 + 1], "fake-pdf" => Encoding.UTF8.GetBytes("<html>not a document</html>"), _ => Pdf },
            invalid == "wrong-mime" ? "text/html" : "application/pdf",
            invalid == "wrong-extension" ? "payload.html" : "contract.pdf",
            invalid switch { "future-date" => DateTime.UtcNow.AddDays(1).ToString("O"), "invalid-date" => "not-a-date", _ => null },
            invalid == "missing-file", invalid == "missing-date"), 400);
        Assert.Null(await Documents.GetAsync(rental.Id, default));
        Assert.Null((await Operations.GetRentalAsync(rental.Id, default))!.SignedAt);
    }

    [Theory]
    [InlineData("image/jpeg", "signed.jpeg", "FFD8FF000000000000000000")]
    [InlineData("image/webp", "signed.webp", "524946460000000057454250")]
    public async Task Accepts_supported_image_signatures(string mime, string name, string hex)
    {
        var (rental, _) = await RentalAsync();
        using var body = await Upload(rental.Id, Form(Convert.FromHexString(hex), mime, name));
        Assert.True((await Operations.GetRentalAsync(rental.Id, default))!.HasUploadedContract);
    }

    [Fact]
    public async Task Exact_ten_MiB_is_accepted()
    {
        var (rental, _) = await RentalAsync();
        var bytes = new byte[10 * 1024 * 1024];
        Pdf.CopyTo(bytes, 0);
        using var body = await Upload(rental.Id, Form(bytes));
        Assert.Equal(bytes.Length, (await Documents.GetAsync(rental.Id, default))!.Content.Length);
    }

    [Theory]
    [InlineData("renting")]
    [InlineData("post_inspection")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public async Task Closed_rental_keeps_original_evidence(string status)
    {
        var (rental, email) = await RentalAsync();
        using var uploaded = await Upload(rental.Id, Form());
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status=$2 WHERE id=$1", rental.Id, status);
        using var body = await Upload(rental.Id, Form(), 409);
        await Assert.ThrowsAsync<ConflictException>(() => Documents.SaveAsync(rental.Id, new(Pdf, "application/pdf", "signed.pdf"), DateTime.UtcNow, new(null, email, "Customer"), default));
        Assert.Equal(Pdf, (await Documents.GetAsync(rental.Id, default))!.Content);
    }

    [Fact]
    public async Task Hourly_missing_document_missing_rental_and_legacy_url_behave_correctly()
    {
        var (hourly, _) = await RentalAsync("hourly");
        using var hourlyBody = await Upload(hourly.Id, Form(), 400);
        using var missingRental = await Upload(int.MaxValue, Form(), 404);
        var (rental, _) = await RentalAsync();
        using var response = await _client.GetAsync($"/api/rentals/{rental.Id}/contract/document");
        using var missing = await Envelope(response, 404);
        await Operations.SaveRentalContractAsync(rental.Id, "https://example.test/legacy.pdf", DateTime.UtcNow.AddMinutes(-2), default);
        Assert.False((await Operations.GetRentalAsync(rental.Id, default))!.HasUploadedContract);
        Assert.False((await new NpgsqlRentalLifecycleRepository(db.DataSource).GetForUpdateAsync(rental.Id, default))!.HasUploadedContract);
        using var uploaded = await Upload(rental.Id, Form());
        Assert.True((await new NpgsqlRentalLifecycleRepository(db.DataSource).GetForUpdateAsync(rental.Id, default))!.HasUploadedContract);
    }

    [Fact]
    public async Task Transactional_handover_requires_uploaded_contract_and_preserves_other_guards()
    {
        var (rental, _) = await RentalAsync();
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='pre_inspection' WHERE id=$1", rental.Id);
        await Seed.ScalarAsync(db.DataSource, "UPDATE invoices SET status='paid' WHERE rental_id=$1", rental.Id);
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO inspections(rental_id,vehicle_id,staff_id,inspection_type,is_ok,inspected_at) VALUES($1,$2,1,'pre_rental',true,now())", rental.Id, rental.VehicleId);
        var repository = new NpgsqlRentalLifecycleRepository(db.DataSource);
        var actor = new ActorContext(1, "operator@test.local", "Admin");
        var failure = await Assert.ThrowsAsync<ConflictException>(() => repository.UpdateToRentingAsync(rental.Id, rental.VehicleId, DateTime.UtcNow, actor, default));
        Assert.Equal("SIGNED_CONTRACT_REQUIRED", failure.Code);
        Assert.Equal("pre_inspection", (await Operations.GetRentalAsync(rental.Id, default))!.Status);
        await Operations.SaveRentalContractAsync(rental.Id, "https://example.test/legacy.pdf", DateTime.UtcNow.AddMinutes(-2), default);
        var legacyFailure = await Assert.ThrowsAsync<ConflictException>(() => repository.UpdateToRentingAsync(rental.Id, rental.VehicleId, DateTime.UtcNow, actor, default));
        Assert.Equal("SIGNED_CONTRACT_REQUIRED", legacyFailure.Code);
        using var uploaded = await Upload(rental.Id, Form());
        await repository.UpdateToRentingAsync(rental.Id, rental.VehicleId, DateTime.UtcNow, actor, default);
        Assert.Equal("renting", (await Operations.GetRentalAsync(rental.Id, default))!.Status);
    }

    [Fact]
    public async Task Swagger_documents_upload_envelope_and_binary_download()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        var upload = paths.GetProperty("/api/rentals/{id}/contract/upload").GetProperty("post");
        var success = upload.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal(6, success.GetProperty("required").GetArrayLength());
        Assert.Equal("boolean", success.GetProperty("properties").GetProperty("is_success").GetProperty("type").GetString());
        var download = paths.GetProperty("/api/rentals/{id}/contract/document").GetProperty("get").GetProperty("responses");
        var content = download.GetProperty("200").GetProperty("content");
        Assert.False(content.TryGetProperty("application/json", out _));
        Assert.Equal("binary", content.GetProperty("application/pdf").GetProperty("schema").GetProperty("format").GetString());
        Assert.Equal(6, download.GetProperty("404").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("required").GetArrayLength());
    }

    public sealed class ContractAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString()), new Claim(ClaimTypes.Email, Request.Headers["X-Test-Email"].ToString())], "test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
        }
    }
}
