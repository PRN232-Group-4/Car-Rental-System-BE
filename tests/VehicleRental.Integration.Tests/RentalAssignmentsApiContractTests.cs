using System.Reflection;
using Car_Rental_System_BE.Application.Assignments;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Controllers;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
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

    [Fact]
    public void Legacy_operations_assign_endpoint_is_marked_obsolete()
    {
        var method = typeof(OperationsController).GetMethod("Assign");
        Assert.NotNull(method);

        var obsolete = method.GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(obsolete);
        Assert.Contains("POST /api/rental/{id}/assignments/driver", obsolete.Message);
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
            ActionName = actionName
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
