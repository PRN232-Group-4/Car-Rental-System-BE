using Car_Rental_System_BE.Application.DTOs.Customers;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

public sealed partial class CustomersController
{
    /// <summary>Admin, Manager or CustomerService retrieves a customer profile by customer ID.</summary>
    [HttpGet("{customerId:int}")]
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
