using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Features.Assignments;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalAssignmentsApiContractTests
{
    [Fact]
    public void Controller_is_configured_with_api_controller_and_route()
    {
        var type = typeof(RentalAssignmentsController);

        Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
        Assert.NotNull(type.GetCustomAttribute<ApiControllerAttribute>());

        var route = type.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("api", route.Template);
    }

    [Theory]
    [InlineData("AssignDriver", typeof(HttpPostAttribute), "rental/{id:int}/assignments/driver")]
    [InlineData("ReplaceVehicle", typeof(HttpPostAttribute), "rental/{id:int}/assignments/vehicle")]
    [InlineData("ListAssignments", typeof(HttpGetAttribute), "rental/{id:int}/assignments")]
    [InlineData("ListAssignableDrivers", typeof(HttpGetAttribute), "rental/{id:int}/assignable-drivers")]
    [InlineData("ListAssignableVehicles", typeof(HttpGetAttribute), "rental/{id:int}/assignable-vehicles")]
    public void Controller_endpoints_have_correct_routes_and_policy(string actionName, Type httpMethodAttr, string template)
    {
        var method = typeof(RentalAssignmentsController).GetMethod(actionName);
        Assert.NotNull(method);

        var attr = method.GetCustomAttribute(httpMethodAttr) as HttpMethodAttribute;
        Assert.NotNull(attr);
        Assert.Equal(template, attr.Template);

        var auth = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(auth);
        Assert.Equal(AuthorizationPolicies.CustomerService, auth.Policy);
    }

    [Fact]
    public void AssignDriver_has_complete_swagger_metadata()
    {
        var method = typeof(RentalAssignmentsController).GetMethod("AssignDriver");
        Assert.NotNull(method);

        var responseTypes = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status200OK && r.Type == typeof(AssignmentResponse));
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status400BadRequest);
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status404NotFound);
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status409Conflict);
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void ReplaceVehicle_has_complete_swagger_metadata()
    {
        var method = typeof(RentalAssignmentsController).GetMethod("ReplaceVehicle");
        Assert.NotNull(method);

        var responseTypes = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status200OK && r.Type == typeof(VehicleReplacementResult));
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status400BadRequest);
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status404NotFound);
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status409Conflict);
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(responseTypes, r => r.StatusCode == StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// The deprecated <c>POST /api/rental/{id}/assignments</c> is deleted for good. Only the GET that owns that
    /// template survives, and it must keep its own route, verb and policy.
    /// </summary>
    [Fact]
    public void Deprecated_operations_assign_action_is_deleted_and_only_the_get_owns_that_template()
    {
        Assert.Null(typeof(OperationsController).GetMethod("Assign"));
        Assert.DoesNotContain(typeof(OperationsController).GetMethods(), method =>
            method.GetCustomAttributes<HttpMethodAttribute>().Any(attribute =>
                attribute.Template == "rental/{id:int}/assignments" && attribute.HttpMethods.Contains("POST")));

        var list = typeof(RentalAssignmentsController).GetMethod("ListAssignments");
        Assert.NotNull(list);
        Assert.Equal("rental/{id:int}/assignments", list.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.DoesNotContain(typeof(RentalAssignmentsController).GetMethods(), method =>
            method.GetCustomAttributes<HttpMethodAttribute>().Any(attribute =>
                attribute.Template == "rental/{id:int}/assignments" && attribute.HttpMethods.Contains("POST")));
    }

    /// <summary>
    /// Routing proof for the deletion: GET still reaches the action, POST is refused by routing (405/404), never 500.
    /// </summary>
    [Fact]
    public async Task Get_assignments_still_routes_while_the_deleted_post_is_refused_by_routing()
    {
        await using var app = BuildApp(new StubAssignmentService());
        using var client = new HttpClient
        {
            BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };

        using var get = new HttpRequestMessage(HttpMethod.Get, "/api/rental/1/assignments");
        get.Headers.Add("X-Test-Role", "Manager");
        using var getResponse = await client.SendAsync(get);
        Assert.Equal(StatusCodes.Status200OK, (int)getResponse.StatusCode);

        using var post = new HttpRequestMessage(HttpMethod.Post, "/api/rental/1/assignments")
        {
            Content = JsonContent.Create(new { driverStaffId = 1 })
        };
        post.Headers.Add("X-Test-Role", "Manager");
        using var postResponse = await client.SendAsync(post);
        // Observed: the GET endpoint owns the template, so the deleted POST is answered with 405 (never 500).
        Assert.Equal(StatusCodes.Status405MethodNotAllowed, (int)postResponse.StatusCode);
    }

    /// <summary>
    /// The three shift actions moved to the assignments slice: they call <see cref="IAssignmentService"/> and never
    /// touch the operations service, while their routes, verbs and policies stay where they were.
    /// </summary>
    [Fact]
    public async Task Dispatch_shift_actions_are_served_by_the_assignments_slice()
    {
        var assignments = new StubAssignmentService();
        var operations = DispatchProxy.Create<IOperationsService, UnusedOperationsService>();
        var controller = new DispatchController(operations, assignments)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Role, "Manager"), new Claim(ClaimTypes.Email, "manager@test.local")], "test"))
                }
            }
        };
        const int driver = 5;
        var from = new DateTime(2032, 6, 1, 9, 0, 0, DateTimeKind.Unspecified);

        var created = Assert.IsType<CreatedResult>(await controller.CreateShift(new DriverShiftRequest(driver, from, from.AddHours(4)), default));
        await controller.UpdateShift(9, new DriverShiftRequest(driver, from, from.AddHours(4), true), default);
        Assert.IsType<OkObjectResult>(await controller.Shifts(from, from.AddHours(4), driver, default));

        Assert.Equal(new int?[] { null, 9 }, assignments.Saved.Select(x => x.Id).ToArray());
        Assert.Equal(driver, assignments.LastListedDriver);
        Assert.Equal("manager@test.local", assignments.Saved[0].Actor);
        Assert.EndsWith("/api/drivers/shift/1", created.Location);
    }

    private static WebApplication BuildApp(IAssignmentService assignments)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Contract").AddScheme<AuthenticationSchemeOptions, UserAdministrationTests.Authentication>("Contract", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton(assignments);
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .AddApplicationPart(typeof(RentalAssignmentsController).Assembly);
        var app = builder.Build();
        app.UseApiResponseErrors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    public class UnusedOperationsService : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class StubAssignmentService : IAssignmentService
    {
        public List<(int? Id, int Driver, DateTime Start, DateTime End, bool Cancelled, string Actor)> Saved { get; } = [];
        public int? LastListedDriver { get; private set; }

        public Task<AssignmentResponse> AssignDriverAsync(DriverAssignmentCommand command, CancellationToken ct) => throw new NotSupportedException();
        public Task<VehicleReplacementResult> ReplaceVehicleAsync(VehicleReplacementCommand command, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssignmentResponse>> GetAssignmentsAsync(int rentalId, CancellationToken ct) => Task.FromResult<IReadOnlyList<AssignmentResponse>>([]);
        public Task<IReadOnlyList<AssignableDriverItem>> ListAssignableDriversAsync(int rentalId, bool eligibleOnly, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssignableVehicleItem>> ListAssignableVehiclesAsync(int rentalId, CancellationToken ct) => throw new NotSupportedException();

        public Task<DriverShift> SaveDriverShiftAsync(int? id, int driverId, DateTime start, DateTime end, bool cancelled, string actor, CancellationToken ct)
        {
            Saved.Add((id, driverId, start, end, cancelled, actor));
            return Task.FromResult(new DriverShift(id ?? 1, driverId, start, end, cancelled ? "cancelled" : "scheduled"));
        }

        public Task<IReadOnlyList<DriverShift>> ListDriverShiftsAsync(int? driverId, DateTime from, DateTime to, CancellationToken ct)
        {
            LastListedDriver = driverId;
            return Task.FromResult<IReadOnlyList<DriverShift>>([]);
        }
    }

    [Theory]
    [InlineData("AssignDriver", "Driver assigned successfully.")]
    [InlineData("ReplaceVehicle", "Vehicle replaced successfully.")]
    [InlineData("ListAssignments", "Rental assignments retrieved successfully.")]
    [InlineData("ListAssignableDrivers", "Assignable drivers retrieved successfully.")]
    [InlineData("ListAssignableVehicles", "Assignable vehicles retrieved successfully.")]
    public async Task ApiSuccessResponseFilter_maps_all_assignment_actions(string actionName, string expectedMessage)
    {
        var filter = new ApiSuccessResponseFilter();
        var httpContext = new DefaultHttpContext();
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerName = "RentalAssignments",
            ActionName = actionName,
            // The wording lives on the action method now, so the descriptor must name the real one.
            MethodInfo = typeof(RentalAssignmentsController).GetMethod(actionName)!
        };

        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), actionDescriptor);
        var resultExecutingContext = new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new OkObjectResult(new { }),
            new object());

        var nextCalled = false;
        await filter.OnResultExecutionAsync(resultExecutingContext, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResultExecutedContext(actionContext, new List<IFilterMetadata>(), resultExecutingContext.Result, new object()));
        });

        Assert.True(nextCalled);
        var okResult = Assert.IsType<OkObjectResult>(resultExecutingContext.Result);
        var response = Assert.IsType<ApiResponse>(okResult.Value);
        Assert.Equal(expectedMessage, response.Message);
    }
}
