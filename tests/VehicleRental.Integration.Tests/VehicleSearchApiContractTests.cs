using System.Reflection;
using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.WebApi.Features.Availability;
using Car_Rental_System_BE.Domain.Features.Availability;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace VehicleRental.Tests;

public sealed class VehicleSearchApiContractTests
{
    [Fact]
    public void Controller_is_configured_with_api_controller_and_route()
    {
        var type = typeof(VehicleSearchController);

        Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
        Assert.NotNull(type.GetCustomAttribute<ApiControllerAttribute>());

        var route = type.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("api", route.Template);
    }

    [Theory]
    [InlineData("Search", typeof(HttpGetAttribute), "vehicles/search", true)]
    [InlineData("Availability", typeof(HttpGetAttribute), "vehicle/{id:int}/availability", true)]
    [InlineData("BusySlots", typeof(HttpGetAttribute), "vehicle/{id:int}/busy-slots", true)]
    [InlineData("ActivePolicy", typeof(HttpGetAttribute), "vehicle-type/{id:int}/active-policy", true)]
    [InlineData("AffectedRentals", typeof(HttpGetAttribute), "vehicle/{id:int}/affected-rentals", false)]
    public void Controller_endpoints_have_correct_routes_and_authorization(string actionName, Type httpMethodAttr, string template, bool isPublic)
    {
        var method = typeof(VehicleSearchController).GetMethod(actionName);
        Assert.NotNull(method);

        var attr = method.GetCustomAttribute(httpMethodAttr) as HttpMethodAttribute;
        Assert.NotNull(attr);
        Assert.Equal(template, attr.Template);

        if (isPublic)
        {
            Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
        }
        else
        {
            var auth = method.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(auth);
            Assert.Equal("Admin,Manager,Staff,CustomerService,Maintenance", auth.Roles);
        }
    }

    [Fact]
    public void Search_endpoint_has_public_search_rate_limiter()
    {
        var method = typeof(VehicleSearchController).GetMethod("Search");
        Assert.NotNull(method);

        var rateLimit = method.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(rateLimit);
        Assert.Equal("public-search", rateLimit.PolicyName);
    }

    [Fact]
    public void Endpoints_have_complete_produces_response_types()
    {
        var methods = new[]
        {
            ("Search", typeof(VehicleSearchResult)),
            ("Availability", typeof(AvailabilityResult)),
            ("BusySlots", typeof(IReadOnlyList<BusySlot>)),
            ("ActivePolicy", typeof(ActiveRentalPolicy)),
            ("AffectedRentals", typeof(IReadOnlyList<AffectedRental>))
        };

        foreach (var (name, returnType) in methods)
        {
            var method = typeof(VehicleSearchController).GetMethod(name);
            Assert.NotNull(method);

            var produces200 = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .FirstOrDefault(a => a.StatusCode == StatusCodes.Status200OK);
            Assert.NotNull(produces200);
            Assert.Equal(returnType, produces200.Type);
        }
    }

    [Fact]
    public void BusySlot_record_does_not_leak_customer_or_rental_pii()
    {
        var properties = typeof(BusySlot).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertyNames = properties.Select(p => p.Name.ToLowerInvariant()).ToList();

        // BusySlot must only contain privacy-safe fields: From, To, Kind
        Assert.Contains("from", propertyNames);
        Assert.Contains("to", propertyNames);
        Assert.Contains("kind", propertyNames);
        Assert.Equal(3, properties.Length);

        // Forbidden sensitive fields (no customer/rental PII)
        var forbiddenWords = new[] { "customer", "user", "email", "name", "phone", "rental", "price", "amount", "cost" };
        foreach (var prop in propertyNames)
        {
            foreach (var forbidden in forbiddenWords)
            {
                Assert.DoesNotContain(forbidden, prop);
            }
        }
    }

    [Theory]
    [InlineData("VehicleSearch.Search", "Vehicles retrieved successfully.")]
    [InlineData("VehicleSearch.Availability", "Vehicle availability retrieved successfully.")]
    [InlineData("VehicleSearch.BusySlots", "Vehicle busy slots retrieved successfully.")]
    [InlineData("VehicleSearch.ActivePolicy", "Active rental policy retrieved successfully.")]
    [InlineData("VehicleSearch.AffectedRentals", "Affected rentals retrieved successfully.")]
    public async Task ApiSuccessResponseFilter_maps_all_vehicle_search_actions(string key, string expectedMessage)
    {
        var parts = key.Split('.');
        var controllerName = parts[0];
        var actionName = parts[1];

        var filter = new ApiSuccessResponseFilter();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";

        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerName = controllerName,
            ActionName = actionName,
            // The wording lives on the action method now, so the descriptor must name the real one.
            MethodInfo = typeof(VehicleSearchController).GetMethod(actionName)!
        };

        var actionContext = new ActionContext(httpContext, new(), actionDescriptor);
        var objectResult = new ObjectResult(new { test = "ok" });
        var resultExecutingContext = new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            objectResult,
            null!);

        var executed = false;
        await filter.OnResultExecutionAsync(resultExecutingContext, () =>
        {
            executed = true;
            return Task.FromResult(new ResultExecutedContext(actionContext, new List<IFilterMetadata>(), objectResult, null!));
        });

        Assert.True(executed);
        var wrapped = Assert.IsType<ApiResponse>(objectResult.Value);
        Assert.Equal(expectedMessage, wrapped.Message);
        Assert.True(wrapped.IsSuccess);
    }

    [Fact]
    public void BusinessRuleException_contains_code_and_status()
    {
        var conflict = new ConflictException("VEHICLE_UNAVAILABLE", "Vehicle is unavailable");
        Assert.Equal("VEHICLE_UNAVAILABLE", conflict.Code);
        Assert.Equal(409, conflict.StatusCode);

        var notFound = new NotFoundException("VEHICLE_NOT_FOUND", "Vehicle not found");
        Assert.Equal("VEHICLE_NOT_FOUND", notFound.Code);
        Assert.Equal(404, notFound.StatusCode);

        var invalid = new BusinessRuleException("INVALID_TIME_RANGE", "Invalid range", 400);
        Assert.Equal("INVALID_TIME_RANGE", invalid.Code);
        Assert.Equal(400, invalid.StatusCode);
    }
}
