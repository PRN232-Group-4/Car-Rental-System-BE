using Car_Rental_System_BE.Application.DTOs.Customers;
using Car_Rental_System_BE.Application.Interfaces.Services;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = "CustomerService")]
public sealed class CustomersController(ICustomerService customers) : ControllerBase
{
    /// <summary>Lists customers for Admin, Manager and CustomerService, including inactive accounts unless filtered.</summary>
    [HttpGet]
    [ProducesResponseType<CustomerPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<CustomerPage> List([FromQuery] CustomerListQuery query, CancellationToken ct) =>
        customers.ListAsync(query, ct);

    /// <summary>Admin, Manager or CustomerService retrieves a customer profile by customer ID.</summary>
    [HttpGet("~/api/customer/{customerId:int}")]
    [ProducesResponseType<CustomerDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute, Range(1, int.MaxValue)] int customerId, CancellationToken ct)
    {
        var result = await customers.FindByIdAsync(customerId, ct);
        return result is null
            ? Problem("Customer was not found.", statusCode: StatusCodes.Status404NotFound)
            : Ok(result);
    }
}
