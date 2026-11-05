using Car_Rental_System_BE.Application.DTOs.Customers;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
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
    [OperationDescription("Admin/Manager/CustomerService only. Optional search, isActive and licenseStatus filters combine before pagination and totalCount. licenseStatus is pending, approved, rejected or none; none means no license number submitted, pending means submitted and awaiting review. Omit filters to list all customers including inactive accounts. Pagination defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Results are ordered by customer ID descending, newest profile first; out-of-range pages return empty items.")]
    [QueryParameterEnum("LicenseStatus", "pending", "approved", "rejected", "none")]
    [SuccessMessage("Customers retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<CustomerPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<CustomerPage> List([FromQuery] CustomerListQuery query, CancellationToken ct) =>
        customers.ListAsync(query, ct);

    /// <summary>Admin, Manager or CustomerService retrieves a customer profile by customer ID.</summary>
    [SuccessMessage("Customer retrieved successfully.")]
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
