using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Application.Loyalty;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

public sealed record LoyaltyAdjustmentApiRequest(
    [Required] int Points,
    [Required, MinLength(5), MaxLength(255)] string Reason,
    string? IdempotencyKey = null
);

[ApiController]
public sealed class LoyaltyController(
    ILoyaltyService loyaltyService,
    ICurrentCustomer currentCustomer
) : ControllerBase
{
    /// <summary>
    /// Retrieves loyalty summary (balance, tier, multiplier, lifetime earned, next tier) for the authenticated customer.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpGet("api/loyalty/me")]
    [ProducesResponseType(typeof(LoyaltySummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMySummary(CancellationToken ct)
    {
        var customerId = await ResolveCurrentCustomerIdAsync(ct);
        var result = await loyaltyService.GetSummaryAsync(customerId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lists paginated loyalty ledger transactions for the authenticated customer.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpGet("api/loyalty/me/transactions")]
    [ProducesResponseType(typeof(PagedLoyaltyTransactionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListMyTransactions(
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var customerId = await ResolveCurrentCustomerIdAsync(ct);
        var (items, total) = await loyaltyService.ListTransactionsAsync(customerId, type, page, pageSize, ct);
        return Ok(new PagedLoyaltyTransactionsResponse(items, total, page, pageSize));
    }

    /// <summary>
    /// Retrieves loyalty summary for a specific customer (Admin, Manager).
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpGet("api/customer/{id:int}/loyalty")]
    [ProducesResponseType(typeof(LoyaltySummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCustomerSummary(int id, CancellationToken ct)
    {
        var result = await loyaltyService.GetSummaryAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Adjusts loyalty points manually for a specific customer with an audit reason (Admin only).
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpPost("api/customer/{id:int}/loyalty/adjustments")]
    [ProducesResponseType(typeof(LoyaltyTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AdjustCustomerPoints(
        int id,
        [FromBody] LoyaltyAdjustmentApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        CancellationToken ct)
    {
        var effectiveKey = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? request.IdempotencyKey
            : idempotencyKeyHeader;

        var email = User.FindFirstValue(ClaimTypes.Email);
        var appRequest = new LoyaltyAdjustmentRequest(request.Points, request.Reason, effectiveKey);
        var result = await loyaltyService.AdjustPointsAsync(id, appRequest, email, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves discount quote for points redemption against rental amount (Customer).
    /// Returns 409 Conflict if redemption is currently disabled in phase 1.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpGet("api/loyalty/redemption-quote")]
    [ProducesResponseType(typeof(RedemptionQuoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetRedemptionQuote([FromQuery] decimal rentAmount, CancellationToken ct)
    {
        var customerId = await ResolveCurrentCustomerIdAsync(ct);
        var result = await loyaltyService.GetRedemptionQuoteAsync(customerId, rentAmount, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves reconciliation discrepancies between cached loyalty points and ledger sums (Admin only).
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpGet("api/admin/loyalty/reconciliation")]
    [ProducesResponseType(typeof(IReadOnlyList<LoyaltyReconciliationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetReconciliation(CancellationToken ct)
    {
        var result = await loyaltyService.GetReconciliationAsync(ct);
        return Ok(result);
    }

    private async Task<int> ResolveCurrentCustomerIdAsync(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            throw new NotFoundException("CUSTOMER_NOT_FOUND", "Customer authentication email was not provided.");

        var customerId = await currentCustomer.GetCustomerIdByEmailAsync(email, ct);
        if (customerId is null)
            throw new NotFoundException("CUSTOMER_NOT_FOUND", $"Customer profile for '{email}' was not found.");

        return customerId.Value;
    }
}
