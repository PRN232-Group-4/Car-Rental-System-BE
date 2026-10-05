using System.Security.Claims;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api/rentals")]
public sealed class RentalQueriesController(
    IRentalQueries rentalQueries,
    ICurrentCustomer currentCustomer
) : ControllerBase
{
    /// <summary>
    /// Lists rentals placed by the currently authenticated customer, filterable by tab (upcoming, ongoing, completed, cancelled).
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpGet("mine")]
    [ProducesResponseType(typeof(PagedResult<CustomerRentalSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Mine(
        [FromQuery] string? tab,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var customerId = await currentCustomer.GetCustomerIdByEmailAsync(email, ct);
        if (customerId is null)
        {
            return Ok(new PagedResult<CustomerRentalSummary>([], 0, page, pageSize));
        }

        var result = await rentalQueries.ListMyRentalsAsync(customerId.Value, tab, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves full customer-facing rental detail including vehicle, driver, pricing, inspections and timeline.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpGet("mine/{id:int}")]
    [ProducesResponseType(typeof(CustomerRentalDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MineDetail(int id, CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var customerId = await currentCustomer.GetCustomerIdByEmailAsync(email, ct);
        if (customerId is null)
        {
            throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
        }

        var detail = await rentalQueries.GetMyRentalDetailAsync(id, customerId.Value, ct);
        if (detail is null)
        {
            throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
        }

        return Ok(detail);
    }

    /// <summary>
    /// Lists operational rentals with search and status filters for staff/management.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpGet("")]
    [ProducesResponseType(typeof(PagedResult<OperationalRentalSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int? customerId,
        [FromQuery] int? vehicleId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filter = new OperationalFilter(status, customerId, vehicleId, from, to, page, pageSize);
        var result = await rentalQueries.ListOperationalRentalsAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves comprehensive operational details for a rental, including settlement and history audit ledger.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpGet("{id:int}/detail")]
    [ProducesResponseType(typeof(OperationalRentalDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var detail = await rentalQueries.GetOperationalDetailAsync(id, ct);
        if (detail is null)
        {
            throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {id} was not found.");
        }

        return Ok(detail);
    }

    /// <summary>
    /// Lists active rentals currently overdue (past scheduled end time) and any next conflicting bookings.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpGet("overdue")]
    [ProducesResponseType(typeof(IReadOnlyList<OverdueRentalItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Overdue(CancellationToken ct)
    {
        var list = await rentalQueries.ListOverdueRentalsAsync(ct);
        return Ok(list);
    }
}
