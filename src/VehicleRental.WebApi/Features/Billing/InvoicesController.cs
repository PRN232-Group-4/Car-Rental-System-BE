using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Billing;

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
    [OperationDescription("Admin/Manager/CustomerService confirms actual full cash receipt. Rental invoices require selected cash method and approval. FR-042: an expired rental is refused with 409 INVOICE_NOT_PAYABLE. Settled overtime invoices may use cash only before checkout_started_at; atomically records overtime ledger with reviewer audit and marks invoice paid without issuing a new order. No client amount or paid flag. Idempotent; conflicting online checkout returns 409.")]
    [SuccessMessage("Cash payment confirmed successfully.")]
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
    [OperationDescription("Admin/Manager/CustomerService creates PayOS link only for an unpaid overtime invoice linked to a completed rental settlement. Rental and invoice locks serialize against cash receipt; reservation blocks cash even on provider failure. Provider signature and amount verification is required before paid. Does not accept customer identity, amount or paid status from client.")]
    [SuccessMessage("Overtime payment link created successfully.")]
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

    [OperationDescription("Customer, own invoice only. Rental invoices require prior staff confirmation (confirmed_at present, rental not pending/cancelled/expired); otherwise 409 RENTAL_CONFIRMATION_REQUIRED, without reserving checkout or calling PayOS. FR-042: an expired request returns 409 RENTAL_EXPIRED before any provider call. Unpaid overtime invoices remain payable after settlement. Payment must complete before handover.")]
    [SuccessMessage("Checkout link created successfully.")]
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

    [SuccessMessage("Invoices retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.FinancialRead)]
    [HttpGet("invoices")]
    [ProducesResponseType<IReadOnlyList<InvoiceSummary>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<InvoiceSummary>> Invoices(CancellationToken ct) => payments.ListInvoicesAsync(Scope(), ct);

    [OperationDescription("Invoice detail includes canPay, paymentBlockedReason, kind, canConfirmCash and canCreateStaffCheckout (booleans). Overtime collection requires a completed rental and linked settlement. Cash is disallowed after online reservation. FR-042: a rental in expired state reports canPay=false with paymentBlockedReason INVOICE_NOT_PAYABLE. Policies are rechecked under locks; online paid status only follows verified provider receipt.")]
    [SuccessMessage("Invoice retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.FinancialRead)]
    [HttpGet("invoice/{invoiceId:int}")]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoice(int invoiceId, CancellationToken ct) => await payments.GetInvoiceAsync(invoiceId, Scope(), ct) is { } x ? Ok(x) : Problem("Invoice was not found.", statusCode: StatusCodes.Status404NotFound);

    [SuccessMessage("Invoice created successfully.")]
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
