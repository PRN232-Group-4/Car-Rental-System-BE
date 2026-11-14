using System.Reflection;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.RentalPolicies;
using Car_Rental_System_BE.Application.Features.Media;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Features.Rentals;
using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalPolicyStatusApiTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private PolicyRepository _repository = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("PolicyTest")
            .AddScheme<AuthenticationSchemeOptions, CustomerDetailApiTests.RoleAuthentication>("PolicyTest", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        var repository = DispatchProxy.Create<IRentalPolicyRepository, PolicyRepository>();
        _repository = (PolicyRepository)(object)repository;
        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton<IRentalPolicyService, RentalPolicyService>();
        // The vehicle endpoints moved to the Fleet slice; this suite never calls them.
        builder.Services.AddSingleton(DispatchProxy.Create<Car_Rental_System_BE.Application.Features.Fleet.IFleetService, UnusedMedia>());
        builder.Services.AddSingleton(DispatchProxy.Create<IMediaService, UnusedMedia>());
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new AssemblyPart(typeof(RentalStatusController).Assembly));
            });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.OperationFilter<ApiSuccessResponseOperationFilter>());
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseSwagger();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Admin", false)]
    [InlineData("Manager", true)]
    [InlineData("Manager", false)]
    public async Task Management_sets_explicit_status_idempotently_and_retains_terms(string role, bool active)
    {
        var before = _repository.Policy;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await Patch(7, JsonSerializer.Serialize(new { isActive = active, vatRate = 99 }), role);
            using var json = await Envelope(response, 200);
            Assert.Equal("Rental policy status updated successfully.", json.RootElement.GetProperty("message").GetString());
            var data = json.RootElement.GetProperty("data");
            Assert.Equal(active, data.GetProperty("isActive").GetBoolean());
            Assert.Equal(before.VatRate, data.GetProperty("vatRate").GetDecimal());
            Assert.Equal(before.DepositPercentage, data.GetProperty("depositPercentage").GetDecimal());
            Assert.Equal(before.OvertimeFeeRate, data.GetProperty("overtimeFeeRate").GetDecimal());
        }
        Assert.Equal(before with { IsActive = active }, _repository.Policy);
        Assert.Equal(2, _repository.Calls);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Staff", 403)]
    [InlineData("CustomerService", 403)]
    [InlineData("Customer", 403)]
    [InlineData("Driver", 403)]
    [InlineData("Maintenance", 403)]
    public async Task Other_roles_cannot_change_policy(string? role, int code)
    {
        using var response = await Patch(7, "{\"isActive\":false}", role);
        using var json = await Envelope(response, code);
        Assert.Equal(0, _repository.Calls);
        Assert.True(_repository.Policy.IsActive);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"isActive\":null}")]
    [InlineData("{\"isActive\":\"false\"}")]
    [InlineData("{\"isActive\":0}")]
    [InlineData("{")]
    public async Task Missing_or_invalid_boolean_returns_validation_before_repository(string body)
    {
        using var response = await Patch(7, body, "Manager");
        using var json = await Envelope(response, 400);
        Assert.True(json.RootElement.GetProperty("error").TryGetProperty("errors", out _));
        Assert.Equal(0, _repository.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_id_is_checked_at_http_and_application_boundaries(int id)
    {
        using var response = await Patch(id, "{\"isActive\":false}", "Admin");
        using var json = await Envelope(response, 400);
        var service = new RentalPolicyService((IRentalPolicyRepository)(object)_repository);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetPolicyStatusAsync(id, false, default));
        Assert.Equal(0, _repository.Calls);
    }

    [Fact]
    public async Task Missing_policy_returns_404_without_changing_another_policy()
    {
        using var response = await Patch(99, "{\"isActive\":false}", "Admin");
        using var json = await Envelope(response, 404);
        Assert.Equal("Rental policy was not found.", json.RootElement.GetProperty("message").GetString());
        Assert.True(_repository.Policy.IsActive);
    }

    [Fact]
    public async Task Swagger_exposes_status_route_required_boolean_and_six_field_responses()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var root = json.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var list = root.GetProperty("paths").GetProperty("/api/rental-policies").GetProperty("get");
        Assert.Contains("rentalCount", list.GetProperty("description").GetString());
        foreach (var name in new[] { "items", "page", "pageSize", "totalCount" })
            Assert.True(schemas.GetProperty("RentalPolicyPage").GetProperty("properties").TryGetProperty(name, out _));
        Assert.Equal("int64", schemas.GetProperty("RentalPolicy").GetProperty("properties").GetProperty("rentalCount").GetProperty("format").GetString());
        Assert.Equal(1, schemas.GetProperty("PolicyRequest").GetProperty("properties").GetProperty("vehicleTypeId").GetProperty("minimum").GetInt32());
        var operation = root.GetProperty("paths").GetProperty("/api/rental-policy/{id}/status").GetProperty("patch");
        Assert.Contains("Admin/Manager", operation.GetProperty("summary").GetString());
        var request = root.GetProperty("components").GetProperty("schemas").GetProperty("PolicyStatusRequest");
        Assert.Contains(request.GetProperty("required").EnumerateArray(), property => property.GetString() == "isActive");
        Assert.Equal("boolean", request.GetProperty("properties").GetProperty("isActive").GetProperty("type").GetString());
        foreach (var code in new[] { "200", "400", "401", "403", "404" })
        {
            var schema = operation.GetProperty("responses").GetProperty(code).GetProperty("content").GetProperty("application/json").GetProperty("schema");
            Assert.Equal(6, schema.GetProperty("required").GetArrayLength());
            Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("is_success").GetProperty("type").GetString());
            Assert.Equal("integer", schema.GetProperty("properties").GetProperty("code").GetProperty("type").GetString());
        }
    }

    private async Task<HttpResponseMessage> Patch(int id, string body, string? role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/rental-policy/{id}/status")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        return await _client.SendAsync(request);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    public async Task Create_checks_type_and_returns_zero_usage(string role)
    {
        using var response = await Post(new { vehicleTypeId = 4, name = "New", vatRate = 10, overtimeFeeRate = 20, depositPercentage = 30 }, role);
        using var json = await Envelope(response, 201);
        Assert.Equal(0, json.RootElement.GetProperty("data").GetProperty("rentalCount").GetInt64());
        Assert.Equal(4, _repository.CheckedType);
        Assert.Equal(1, _repository.Creates);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public async Task Missing_or_nonpositive_type_returns_field_error_without_inserting(int typeId)
    {
        using var response = await Post(new { vehicleTypeId = typeId, name = "New", vatRate = 10, overtimeFeeRate = 20, depositPercentage = 30 });
        using var json = await Envelope(response, 400);
        Assert.Contains(json.RootElement.GetProperty("error").GetProperty("errors").EnumerateObject(), field => field.Name.Equals("VehicleTypeId", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, _repository.Creates);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(null)]
    public async Task Active_policy_in_same_scope_blocks_creation_including_global_scope(int? typeId)
    {
        _repository.ScopeActive = true;
        using var response = await Post(new { vehicleTypeId = typeId, name = "New", vatRate = 10, overtimeFeeRate = 20, depositPercentage = 30 });
        using var json = await Envelope(response, 400);
        Assert.True(json.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("VehicleTypeId", out _));
        Assert.Equal(0, _repository.Creates);
    }

    [Fact]
    public async Task Global_scope_is_allowed_without_a_type_lookup_when_no_active_policy_exists()
    {
        using var response = await Post(new { vehicleTypeId = (int?)null, name = "Global", vatRate = 0, overtimeFeeRate = 100, depositPercentage = 0 });
        using var json = await Envelope(response, 201);
        Assert.Null(_repository.CheckedType);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").GetProperty("vehicleTypeId").ValueKind);
    }

    [Fact]
    public async Task Reference_or_scope_changing_between_checks_and_insert_still_maps_to_inline_field_error()
    {
        _repository.FailAtInsert = true;
        using var response = await Post(new { vehicleTypeId = 4, name = "New", vatRate = 10, overtimeFeeRate = 20, depositPercentage = 30 });
        using var json = await Envelope(response, 400);
        Assert.True(json.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("VehicleTypeId", out _));
    }

    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(1, 100, 1, 100)]
    [InlineData(999, 20, 999, 20)]
    [InlineData(int.MaxValue, 100, int.MaxValue, 100)]
    public async Task List_returns_page_metadata_and_per_policy_usage_including_empty_pages(int? page, int? size, int expectedPage, int expectedSize)
    {
        var query = page is null ? "" : $"?page={page}&pageSize={size}";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/rental-policies" + query);
        request.Headers.Add("X-Test-Role", "Manager");
        using var response = await _client.SendAsync(request);
        using var json = await Envelope(response, 200);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(expectedPage, data.GetProperty("page").GetInt32());
        Assert.Equal(expectedSize, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(105, data.GetProperty("totalCount").GetInt32());
        var items = data.GetProperty("items");
        Assert.Equal(expectedPage > 105 ? 0 : expectedSize, items.GetArrayLength());
        if (items.GetArrayLength() > 0) Assert.Equal(210L, items[0].GetProperty("rentalCount").GetInt64());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Pagination_is_validated_at_http_and_application_boundaries(int page, int size)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/rental-policies?page={page}&pageSize={size}");
        request.Headers.Add("X-Test-Role", "Admin");
        using var response = await _client.SendAsync(request);
        using var json = await Envelope(response, 400);
        var service = new RentalPolicyService((IRentalPolicyRepository)(object)_repository);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ListPoliciesAsync(page, size, default));
        Assert.Null(_repository.LastPage);
    }

    [Theory]
    [InlineData(0, "Name", 10)]
    [InlineData(4, "", 10)]
    [InlineData(4, "Name", 101)]
    public async Task Application_rejects_invalid_policy_before_repository(int typeId, string name, decimal vat)
    {
        var service = new RentalPolicyService((IRentalPolicyRepository)(object)_repository);
        await Assert.ThrowsAsync<ValidationException>(() => service.CreatePolicyAsync(new RentalPolicy(0, typeId, name, vat, 20, 30, true), default));
        Assert.Equal(0, _repository.Creates);
        Assert.Null(_repository.CheckedType);
    }

    private async Task<HttpResponseMessage> Post(object body, string role = "Manager")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rental-policies") { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Test-Role", role);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> Envelope(HttpResponseMessage response, int code)
    {
        Assert.Equal(code, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, root.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal(code, root.GetProperty("code").GetInt32());
        Assert.Equal(code < 300 ? "success" : "error", root.GetProperty("status").GetString());
        Assert.Equal(code < 300 ? JsonValueKind.True : JsonValueKind.False, root.GetProperty("is_success").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty(code < 300 ? "error" : "data").ValueKind);
        return json;
    }

    public class PolicyRepository : DispatchProxy
    {
        public RentalPolicy Policy { get; private set; } = new(7, null, "Policy", 10m, 20m, 30m, true);
        public int Calls { get; private set; }
        public int Creates { get; private set; }
        public int? CheckedType { get; private set; }
        public int? LastPage { get; private set; }
        public bool ScopeActive { get; set; }
        public bool FailAtInsert { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "VehicleTypeExistsAsync": CheckedType = (int)args![0]!; return Task.FromResult(CheckedType == 4);
                case "HasActivePolicyAsync": return Task.FromResult(ScopeActive);
                case "CreatePolicyAsync":
                    if (FailAtInsert) throw RentalPolicyValidator.InvalidVehicleType();
                    Creates++;
                    return Task.FromResult(((RentalPolicy)args![0]!) with { Id = 8, RentalCount = 0 });
                case "ListPoliciesAsync":
                    LastPage = (int)args![0]!;
                    var size = (int)args[1]!;
                    var offset = ((long)LastPage.Value - 1) * size;
                    var items = Enumerable.Range(1, 105).Reverse().Skip((int)Math.Min(offset, 105)).Take(size).Select(id => Policy with { Id = id, RentalCount = id * 2L }).ToArray();
                    return Task.FromResult(new RentalPolicyPage(items, LastPage.Value, size, 105));
            }
            Assert.Equal("SetPolicyStatusAsync", method!.Name);
            Calls++;
            if ((int)args![0]! != Policy.Id) return Task.FromResult<RentalPolicy?>(null);
            Policy = Policy with { IsActive = (bool)args[1]! };
            return Task.FromResult<RentalPolicy?>(Policy);
        }
    }

    public class UnusedMedia : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException();
    }
}
