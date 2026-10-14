using Car_Rental_System_BE.Application.DTOs.Customers;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = "CustomerService")]
public sealed partial class CustomersController(ICustomerService customers) : ControllerBase
{
    /// <summary>Lists customers for Admin, Manager and CustomerService, including inactive accounts unless filtered.</summary>
    [HttpGet]
    [ProducesResponseType<CustomerPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<CustomerPage> List([FromQuery] CustomerListQuery query, CancellationToken ct) =>
        customers.ListAsync(query, ct);
}
