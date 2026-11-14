using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Rentals;

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
    [OperationDescription("Pagination defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid pagination returns 400. data contains items/page/pageSize/totalCount with the count after filtering and before pagination. Results are ordered by rental ID descending; out-of-range pages return empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Each item includes nullable orderId for the official paid rental order; id remains the booking request ID. Omit status (and tab for mine) to include all lifecycle states, including completed and cancelled.")]
    [SuccessMessage("Your rentals retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpGet("mine")]
    [ProducesResponseType(typeof(PagedResult<CustomerRentalSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Mine(
        [FromQuery] string? tab,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken ct = default)
    {
        RentalQueryValidation.Pagination(page, pageSize);
        RentalQueryValidation.CustomerTab(tab);
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
    [OperationDescription("Rental detail includes status, audit timeline/history and nullable top-level orderId. id (or rental.id) remains the booking request ID; only a persisted official order supplies orderId. A cancelled rental remains cancelled even if a historical paid order exists.")]
    [SuccessMessage("Your rental details retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpGet("~/api/rental/mine/{id:int}")]
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
    [OperationDescription("Pagination defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid pagination returns 400. data contains items/page/pageSize/totalCount with the count after filtering and before pagination. Results are ordered by rental ID descending; out-of-range pages return empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Each item includes nullable orderId for the official paid rental order; id remains the booking request ID. Omit status (and tab for mine) to include all lifecycle states, including completed and cancelled.")]
    [OperationDescription(" status=overdue filters renting rentals with no returned_at and end time before the server clock, before count/pagination. status=expired is a valid lifecycle filter (FR-042). Summaries expose isOverdue; lifecycle status remains renting so return remains valid. Newest id first, defaults page=1/pageSize=20, maximum 100.")]
    [SuccessMessage("Rentals retrieved successfully.")]
    // Maintenance reads the list too: technicians pick rentals waiting for pre/post-rental inspection from it.
    [Authorize(Roles = "Admin,Manager,CustomerService,Maintenance")]
    [HttpGet("")]
    [ProducesResponseType(typeof(PagedResult<OperationalRentalSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery, Range(1, int.MaxValue)] int? customerId,
        [FromQuery, Range(1, int.MaxValue)] int? vehicleId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filter = new OperationalFilter(status, customerId, vehicleId, from, to, page, pageSize);
        RentalQueryValidation.Operational(filter);
        var result = await rentalQueries.ListOperationalRentalsAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves comprehensive operational details for a rental, including settlement and history audit ledger.
    /// </summary>
    [OperationDescription("Rental detail includes status, audit timeline/history and nullable top-level orderId. id (or rental.id) remains the booking request ID; only a persisted official order supplies orderId. A cancelled rental remains cancelled even if a historical paid order exists.")]
    [SuccessMessage("Rental details retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpGet("~/api/rental/{id:int}/detail")]
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
    [SuccessMessage("Overdue rentals retrieved successfully.")]
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
