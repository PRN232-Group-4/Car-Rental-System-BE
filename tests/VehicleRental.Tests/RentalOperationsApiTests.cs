using System.Reflection;
using System.Text;
using System.Text.Json;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Handovers;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalOperationsApiTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private QueryRepository _repository = null!;
    private EditRepository _editing = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("RentalTest")
            .AddScheme<AuthenticationSchemeOptions, CustomerDetailApiTests.RoleAuthentication>("RentalTest", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        var repository = DispatchProxy.Create<IRentalQueryRepository, QueryRepository>();
        _repository = (QueryRepository)(object)repository;
        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton<IRentalQueries, RentalQueryService>();
        builder.Services.AddSingleton(DispatchProxy.Create<ICurrentCustomer, CurrentCustomer>());
        builder.Services.AddSingleton(DispatchProxy.Create<IRentalLifecycleService, Lifecycle>());
        var editing = DispatchProxy.Create<IRentalEditingRepository, EditRepository>();
        _editing = (EditRepository)(object)editing;
        builder.Services.AddSingleton(editing);
        builder.Services.AddSingleton<IRentalEditingService, RentalEditingService>();
        builder.Services.AddSingleton<IHandoverService, HandoverService>();
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new AssemblyPart(typeof(RentalQueriesController).Assembly));
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
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("CustomerService")]
    public async Task Lists_are_paginated_with_contextual_messages_and_filters(string role)
    {
        using var response = await Send(HttpMethod.Get, "/api/rentals?customerId=7&status=pending", role);
        using var json = await Envelope(response, 200);
        Assert.Equal("Rentals retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        var page = json.RootElement.GetProperty("data");
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(7, _repository.LastFilter!.CustomerId);
        Assert.Equal("pending", _repository.LastFilter.Status);
    }

    [Theory]
    [InlineData("/api/rentals?page=0")]
    [InlineData("/api/rentals?pageSize=101")]
    [InlineData("/api/rentals?customerId=0")]
    [InlineData("/api/rentals?status=unknown")]
    [InlineData("/api/rentals/mine?pageSize=0")]
    [InlineData("/api/rentals/mine?tab=unknown")]
    [InlineData("/api/handovers?page=0")]
    [InlineData("/api/handovers?pageSize=101")]
    [InlineData("/api/handovers?status=unknown")]
    public async Task Invalid_filters_are_rejected_before_repository(string path)
    {
        using var response = await Send(HttpMethod.Get, path, path.Contains("/mine") ? "Customer" : "Manager");
        using var json = await Envelope(response, 400);
        Assert.Equal(0, _repository.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Valid_page_size_boundaries_and_empty_page_keep_metadata(int pageSize)
    {
        using var response = await Send(HttpMethod.Get, $"/api/rentals?page=2147483647&pageSize={pageSize}", "Manager");
        using var json = await Envelope(response, 200);
        var data = json.RootElement.GetProperty("data");
        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(int.MaxValue, data.GetProperty("page").GetInt32());
        Assert.Equal(pageSize, data.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Customer", 403)]
    [InlineData("Staff", 403)]
    [InlineData("Maintenance", 403)]
    [InlineData("Driver", 403)]
    public async Task Operational_routes_enforce_roles(string? role, int code)
    {
        foreach (var (method, path, body) in new[] {
            (HttpMethod.Get, "/api/rentals", (string?)null),
            (HttpMethod.Get, "/api/handovers", (string?)null),
            (HttpMethod.Post, "/api/handovers/7/confirm", (string?)null),
            (HttpMethod.Put, "/api/rentals/7", ValidEdit) })
        {
            using var response = await Send(method, path, role, body);
            using var json = await Envelope(response, code);
        }
        Assert.Equal(0, _repository.Calls);
        Assert.Equal(0, _editing.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("{\"vehicleId\":1,\"startTime\":\"2032-07-01T08:00:00Z\",\"endTime\":\"2032-07-01T12:00:00Z\"}")]
    public async Task Missing_edit_fields_or_malformed_body_return_400(string body)
    {
        using var response = await Send(HttpMethod.Put, "/api/rentals/7", "Manager", body);
        using var json = await Envelope(response, 400);
        Assert.True(json.RootElement.GetProperty("error").TryGetProperty("errors", out _));
        Assert.Equal(0, _editing.Calls);
    }

    [Fact]
    public async Task Edit_and_handover_return_single_envelope_and_business_messages()
    {
        using var updated = await Send(HttpMethod.Put, "/api/rentals/7", "CustomerService", ValidEdit);
        using var edit = await Envelope(updated, 200);
        Assert.Equal("Rental updated successfully.", edit.RootElement.GetProperty("message").GetString());
        Assert.Equal(7, edit.RootElement.GetProperty("data").GetProperty("id").GetInt32());
        using var handedOver = await Send(HttpMethod.Post, "/api/handovers/7/confirm", "Manager");
        using var handover = await Envelope(handedOver, 200);
        Assert.Equal("Vehicle handover confirmed successfully.", handover.RootElement.GetProperty("message").GetString());
        var data = handover.RootElement.GetProperty("data");
        Assert.Equal("completed", data.GetProperty("status").GetString());
        Assert.Equal("renting", data.GetProperty("rentalStatus").GetString());
        Assert.False(data.TryGetProperty("is_success", out _));
    }

    [Fact]
    public async Task Editing_after_start_returns_conflict_envelope()
    {
        using var response = await Send(HttpMethod.Put, "/api/rentals/9", "Manager", ValidEdit);
        using var json = await Envelope(response, 409);
        Assert.Contains("not started", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("RENTAL_NOT_EDITABLE", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(409, json.RootElement.GetProperty("error").GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("completed")]
    [InlineData("all")]
    public async Task Handover_list_passes_status_filters_and_preserves_page(string status)
    {
        using var response = await Send(HttpMethod.Get, $"/api/handovers?status={status}&customerId=7&pageSize=1", "Manager");
        using var json = await Envelope(response, 200);
        Assert.Equal(status, _repository.LastFilter!.HandoverStatus);
        Assert.Null(_repository.LastFilter.Status);
        Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Swagger_documents_new_routes_pagination_and_required_envelope()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths");
        foreach (var (path, method) in new[] { ("/api/rentals/{id}", "put"), ("/api/handovers", "get"), ("/api/handovers/{rentalId}", "get"), ("/api/handovers/{rentalId}/confirm", "post") })
        {
            var responses = paths.GetProperty(path).GetProperty(method).GetProperty("responses");
            foreach (var code in new[] { "200", "400", "401", "403", "500" })
            {
                var schema = responses.GetProperty(code).GetProperty("content").GetProperty("application/json").GetProperty("schema");
                Assert.Equal(6, schema.GetProperty("required").GetArrayLength());
                Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("is_success").GetProperty("type").GetString());
            }
        }
        var parameters = paths.GetProperty("/api/handovers").GetProperty("get").GetProperty("parameters");
        var size = parameters.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "pageSize").GetProperty("schema");
        Assert.Equal(1, size.GetProperty("minimum").GetInt32());
        Assert.Equal(100, size.GetProperty("maximum").GetInt32());
        Assert.Equal(20, size.GetProperty("default").GetInt32());
    }

    private const string ValidEdit = "{\"vehicleId\":1,\"startTime\":\"2032-07-01T08:00:00Z\",\"endTime\":\"2032-07-01T12:00:00Z\",\"hasDriver\":false}";

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? role, string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> Envelope(HttpResponseMessage response, int code)
    {
        Assert.Equal(code, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(6, root.EnumerateObject().Count());
        Assert.Equal(code, root.GetProperty("code").GetInt32());
        Assert.Equal(code < 300, root.GetProperty("is_success").GetBoolean());
        Assert.Equal(code < 300 ? "success" : "error", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty(code < 300 ? "error" : "data").ValueKind);
        return json;
    }

    public class QueryRepository : DispatchProxy
    {
        public int Calls { get; private set; }
        public OperationalFilter? LastFilter { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            if (method!.Name == "ListOperationalRentalsAsync")
            {
                var filter = (OperationalFilter)args![0]!;
                LastFilter = filter;
                OperationalRentalSummary item = new(7, 7, "Customer", "customer@test.local", 1, "TEST", "Car", "pre_inspection", DateTime.UtcNow, DateTime.UtcNow.AddHours(4), 440000, false, null);
                return Task.FromResult(new PagedResult<OperationalRentalSummary>(filter.Page > 1 ? [] : [item], 1, filter.Page, filter.PageSize));
            }
            throw new InvalidOperationException(method.Name);
        }
    }

    public class CurrentCustomer : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Task.FromResult<int?>(7);
    }

    public class Lifecycle : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("StartAsync", method!.Name);
            return Task.FromResult(new RentalLifecycleResult((int)args![0]!, "pre_inspection", "renting", "Rental started successfully."));
        }
    }

    public class EditRepository : DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            var id = (int)args![0]!;
            RentalEditingRules.RequireEditable(id == 9 ? "renting" : "pending", id == 9 ? DateTime.UtcNow : null);
            return Task.FromResult(new Rental(id, 7, 1, "hourly", "pending", false, 40000, 100000, 0, 440000, DateTime.UtcNow, DateTime.UtcNow.AddHours(4)));
        }
    }
}
