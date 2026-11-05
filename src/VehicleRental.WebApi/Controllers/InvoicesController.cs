using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Billing;
using Car_Rental_System_BE.Application.Services.Exceptions;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

/// <summary>
/// The invoice half of the former PaymentsController. Routes, verbs, policies and response contracts are
/// byte-identical to PaymentsController.cs:19-155; only the owning controller (and therefore the envelope
/// message key, Invoices.*) changed.
/// </summary>
[ApiController]
[Route("api")]
public sealed class InvoicesController(IPaymentService payments) : ControllerBase
{
    /// <summary>Staff confirms actual cash receipt after rental approval; atomically issues the paid order.</summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("invoice/{invoiceId:int}/cash-confirmation")]
    [ProducesResponseType<PaymentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<PaymentResult> ConfirmCash([FromRoute, Range(1, int.MaxValue)] int invoiceId, CancellationToken ct) => payments.ConfirmCashAsync(invoiceId, Email(), ct);

    /// <summary>Creates a PayOS checkout for an unpaid, settled overtime invoice; only provider verification marks paid.</summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("invoice/{invoiceId:int}/staff-checkout")]
    [ProducesResponseType<CheckoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StaffCheckout([FromRoute, Range(1, int.MaxValue)] int invoiceId, CancellationToken ct)
    {
        try { return Ok(await payments.CheckoutOvertimeForStaffAsync(invoiceId, ct)); }
        catch (PaymentWorkflowException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpPost("invoice/{invoiceId:int}/checkout")]
    [ProducesResponseType<CheckoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Checkout(int invoiceId, CancellationToken ct)
    {
        try { return Ok(await payments.CheckoutAsync(invoiceId, Email(), ct)); }
        catch (PaymentWorkflowException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [Authorize(Policy = AuthorizationPolicies.FinancialRead)]
    [HttpGet("invoices")]
    [ProducesResponseType<IReadOnlyList<InvoiceSummary>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<InvoiceSummary>> Invoices(CancellationToken ct) => payments.ListInvoicesAsync(Scope(), ct);

    [Authorize(Policy = AuthorizationPolicies.FinancialRead)]
    [HttpGet("invoice/{invoiceId:int}")]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoice(int invoiceId, CancellationToken ct) => await payments.GetInvoiceAsync(invoiceId, Scope(), ct) is { } x ? Ok(x) : Problem("Invoice was not found.", statusCode: StatusCodes.Status404NotFound);

    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("rental/{rentalId:int}/invoice")]
    [ProducesResponseType<InvoiceSummary>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateInvoice(int rentalId, CancellationToken ct)
    {
        var invoice = await payments.CreateInvoiceForRentalAsync(rentalId, ct);
        return Created($"/api/invoice/{invoice.Id}", invoice);
    }

    private string Email() => User.FindFirstValue(ClaimTypes.Email) ?? throw new UnauthorizedAccessException();
    private string? Scope() => AuthorizationPolicies.CanServeCustomers(User) ? null : Email();
}
