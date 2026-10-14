using Car_Rental_System_BE.Application.Common.Errors;
using VehicleRental.Domain.Lifecycle;

namespace Car_Rental_System_BE.Application.Lifecycle;

public sealed class RentalLifecycleService(
    IRentalLifecycleRepository repository,
    IRentalPaymentGate paymentGate,
    IInvoiceGateway invoiceGateway,
    ISubscriptionUsage subscriptionUsage,
    ILoyaltyAward loyaltyAward,
    IRentalEventSink eventSink,
    RentalOptions rentalOptions,
    RentalCancellationOptions cancellationOptions,
    TimeProvider timeProvider
) : IRentalLifecycleService
{
    private readonly RentalOptions _options = rentalOptions;
    private readonly RentalCancellationOptions _cancelOptions = cancellationOptions;

    public async Task<RentalLifecycleResult> ConfirmAsync(int id, ActorContext actor, CancellationToken ct)
    {
        var rental = await repository.GetForUpdateAsync(id, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {id} was not found.");

        var outcome = RentalStateMachine.Evaluate(rental.Status, RentalTrigger.Confirm);
        if (outcome is TransitionOutcome.AlreadyInState)
        {
            throw new ConflictException("ALREADY_IN_STATE", $"Rental is already in {rental.Status.ToDbString()} status.");
        }

        if (outcome is TransitionOutcome.IllegalTransition illegal)
        {
            var allowed = string.Join(", ", illegal.AllowedNextStatuses.Select(s => s.ToDbString()));
            throw new ConflictException("ILLEGAL_TRANSITION", $"Cannot transition from {rental.Status.ToDbString()} using confirm. Allowed next: [{allowed}]");
        }

        if (!rental.IsSubscription)
        {
            var isPaid = await paymentGate.IsRentalInvoicePaidAsync(id, ct);
            if (!isPaid)
            {
                throw new ConflictException("PAYMENT_REQUIRED", "Direct rental must be paid before confirmation.");
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await repository.UpdateToPreInspectionAsync(id, now, actor, ct);

        await eventSink.PublishStatusChangedAsync(
            new RentalStatusChangedEvent(id, rental.Status, RentalStatus.PreInspection, now, actor), ct);

        return new RentalLifecycleResult(id, rental.Status.ToDbString(), RentalStatus.PreInspection.ToDbString(), "Rental confirmed successfully.");
    }

    public async Task<RentalLifecycleResult> StartAsync(int id, ActorContext actor, CancellationToken ct)
    {
        var rental = await repository.GetForUpdateAsync(id, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {id} was not found.");

        var outcome = RentalStateMachine.Evaluate(rental.Status, RentalTrigger.Start);
        if (outcome is TransitionOutcome.AlreadyInState)
        {
            throw new ConflictException("ALREADY_IN_STATE", $"Rental is already in {rental.Status.ToDbString()} status.");
        }

        if (outcome is TransitionOutcome.IllegalTransition illegal)
        {
            var allowed = string.Join(", ", illegal.AllowedNextStatuses.Select(s => s.ToDbString()));
            throw new ConflictException("ILLEGAL_TRANSITION", $"Cannot transition from {rental.Status.ToDbString()} using start. Allowed next: [{allowed}]");
        }

        var hasPreInspection = await repository.HasPassingPreInspectionAsync(id, ct);
        if (!hasPreInspection)
        {
            throw new ConflictException("PRE_INSPECTION_REQUIRED", "A successful pre-rental inspection is required to start.");
        }

        var hasAssignment = await repository.HasValidAssignmentAsync(id, rental.HasDriver, ct);
        if (!hasAssignment)
        {
            throw new ConflictException("ASSIGNMENT_REQUIRED", "Vehicle and driver (if requested) must be assigned.");
        }

        if (!rental.HasDriver)
        {
            var licenseValid = await repository.IsCustomerLicenseValidAsync(rental.CustomerId, ct);
            if (!licenseValid)
            {
                throw new ConflictException("CUSTOMER_LICENSE_NOT_VALID", "Customer driver license must be approved and not expired for self-drive.");
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await repository.UpdateToRentingAsync(id, rental.VehicleId, now, actor, ct);

        await eventSink.PublishStatusChangedAsync(
            new RentalStatusChangedEvent(id, rental.Status, RentalStatus.Renting, now, actor), ct);

        return new RentalLifecycleResult(id, rental.Status.ToDbString(), RentalStatus.Renting.ToDbString(), "Rental started successfully.");
    }

    public async Task<RentalLifecycleResult> ReturnAsync(int id, DateTime? returnedAt, ActorContext actor, CancellationToken ct)
    {
        var rental = await repository.GetForUpdateAsync(id, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {id} was not found.");

        var outcome = RentalStateMachine.Evaluate(rental.Status, RentalTrigger.Return);
        if (outcome is TransitionOutcome.AlreadyInState)
        {
            throw new ConflictException("ALREADY_IN_STATE", $"Rental is already in {rental.Status.ToDbString()} status.");
        }

        if (outcome is TransitionOutcome.IllegalTransition illegal)
        {
            var allowed = string.Join(", ", illegal.AllowedNextStatuses.Select(s => s.ToDbString()));
            throw new ConflictException("ILLEGAL_TRANSITION", $"Cannot transition from {rental.Status.ToDbString()} using return. Allowed next: [{allowed}]");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var effectiveReturnedAt = returnedAt ?? now;
        var minTime = rental.StartedAt ?? rental.ScheduledStart;
        if (effectiveReturnedAt < minTime || effectiveReturnedAt > now.AddMinutes(5)) // small tolerance for clock skew
        {
            throw new ConflictException("INVALID_RETURNED_AT", "Returned time must be between start time and current time.");
        }

        await repository.UpdateToPostInspectionAsync(id, effectiveReturnedAt, actor, ct);

        await eventSink.PublishStatusChangedAsync(
            new RentalStatusChangedEvent(id, rental.Status, RentalStatus.PostInspection, now, actor), ct);

        return new RentalLifecycleResult(id, rental.Status.ToDbString(), RentalStatus.PostInspection.ToDbString(), "Vehicle returned and moved to post-inspection.");
    }

    public async Task<RentalLifecycleResult> CompleteAsync(int id, ActorContext actor, CancellationToken ct)
    {
        var rental = await repository.GetForUpdateAsync(id, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {id} was not found.");

        var outcome = RentalStateMachine.Evaluate(rental.Status, RentalTrigger.Complete);
        if (outcome is TransitionOutcome.AlreadyInState)
        {
            throw new ConflictException("ALREADY_IN_STATE", $"Rental is already in {rental.Status.ToDbString()} status.");
        }

        if (outcome is TransitionOutcome.IllegalTransition illegal)
        {
            var allowed = string.Join(", ", illegal.AllowedNextStatuses.Select(s => s.ToDbString()));
            throw new ConflictException("ILLEGAL_TRANSITION", $"Cannot transition from {rental.Status.ToDbString()} using complete. Allowed next: [{allowed}]");
        }

        var hasPostInspection = await repository.HasPostInspectionAsync(id, ct);
        if (!hasPostInspection)
        {
            throw new ConflictException("POST_INSPECTION_REQUIRED", "Post-rental inspection must be recorded before completing rental.");
        }

        var isPostInspectionOk = await repository.IsPostInspectionOkAsync(id, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var effectiveReturnedAt = rental.ReturnedAt ?? now;

        // Overtime calculation
        var unitHourly = rental.RentalType == "hourly"
            ? rental.UnitHourlyPrice
            : MoneyRounding.RoundVnd(rental.MonthlyPriceSnapshot / 720m);

        var overtime = OvertimeCalculator.Calculate(
            scheduledEnd: rental.ScheduledEnd,
            returnedAt: effectiveReturnedAt,
            unitHourlyPrice: unitHourly,
            overtimeFeeRate: rental.OvertimeFeeRate,
            vatRate: rental.VatRate,
            graceMinutes: _options.GraceMinutes
        );

        // Deposit held
        var depositHeld = await paymentGate.GetDepositHeldAsync(id, ct);
        var settlementCalc = SettlementCalculator.Calculate(depositHeld, overtime.OvertimeTotal);

        int? shortfallInvoiceId = null;
        if (settlementCalc.Shortfall > 0)
        {
            if (!rental.IsSubscription)
            {
                var dueAt = now.AddHours(72);
                shortfallInvoiceId = await invoiceGateway.CreateOvertimeInvoiceAsync(
                    id, settlementCalc.Shortfall, overtime.OvertimeVat, dueAt, ct);
            }
            else
            {
                // Subscription shortfall handling
                await subscriptionUsage.DeductRemainingHoursAsync(
                    rental.CustomerSubscriptionId ?? 0, overtime.OvertimeHours, ct);
            }
        }

        int? refundPaymentId = null;
        if (settlementCalc.RefundAmount > 0)
        {
            var primaryInvoiceId = await paymentGate.GetPrimaryInvoiceIdAsync(id, ct);
            refundPaymentId = await paymentGate.CreateRefundPaymentAsync(
                id, rental.CustomerId, primaryInvoiceId, settlementCalc.RefundAmount, rental.IsSubscription, ct);
        }

        var settlementDto = new RentalSettlementDto(
            RentalId: id,
            ScheduledEnd: rental.ScheduledEnd,
            ReturnedAt: effectiveReturnedAt,
            OvertimeMinutes: overtime.OvertimeMinutes,
            OvertimeHours: overtime.OvertimeHours,
            OvertimeAmount: overtime.OvertimeAmount,
            OvertimeVat: overtime.OvertimeVat,
            DepositHeld: depositHeld,
            DepositDeducted: settlementCalc.DepositDeducted,
            ShortfallInvoiceId: shortfallInvoiceId,
            RefundPaymentId: refundPaymentId,
            DamageReviewRequired: !isPostInspectionOk,
            CreatedAt: now
        );

        var newVatPrice = rental.VatPrice + overtime.OvertimeVat;
        var newTotalPrice = rental.TotalPrice + overtime.OvertimeTotal;

        await repository.UpdateToCompletedAsync(
            id, rental.VehicleId, now, effectiveReturnedAt, overtime.OvertimeAmount, newVatPrice, newTotalPrice, overtime.OvertimeHours, settlementDto, actor, ct);

        // Hooks in transaction order
        if (rental.IsSubscription && rental.CustomerSubscriptionId.HasValue && overtime.OvertimeHours > 0)
        {
            await subscriptionUsage.DeductRemainingHoursAsync(rental.CustomerSubscriptionId.Value, overtime.OvertimeHours, ct);
        }

        if (!rental.IsSubscription)
        {
            var netRentBasis = Math.Max(0m, newTotalPrice - newVatPrice - rental.DepositPrice);
            await loyaltyAward.AwardPointsAsync(rental.CustomerId, id, netRentBasis, ct);
        }

        await eventSink.PublishStatusChangedAsync(
            new RentalStatusChangedEvent(id, rental.Status, RentalStatus.Completed, now, actor), ct);

        return new RentalLifecycleResult(id, rental.Status.ToDbString(), RentalStatus.Completed.ToDbString(), "Rental completed and settled successfully.", settlementDto);
    }

    public async Task<RentalLifecycleResult> CancelAsync(int id, CancelRentalCommand command, ActorContext actor, CancellationToken ct)
    {
        var rental = await repository.GetForUpdateAsync(id, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {id} was not found.");

        var outcome = RentalStateMachine.Evaluate(rental.Status, RentalTrigger.Cancel);
        if (outcome is TransitionOutcome.AlreadyInState)
        {
            throw new ConflictException("ALREADY_IN_STATE", $"Rental is already in {rental.Status.ToDbString()} status.");
        }

        if (outcome is TransitionOutcome.IllegalTransition illegal)
        {
            var allowed = string.Join(", ", illegal.AllowedNextStatuses.Select(s => s.ToDbString()));
            throw new ConflictException("ILLEGAL_TRANSITION", $"Cannot transition from {rental.Status.ToDbString()} using cancel. Allowed next: [{allowed}]");
        }

        var cancellationActor = actor.Role switch
        {
            "Customer" => CancellationActor.Customer,
            "System" => CancellationActor.System,
            _ => CancellationActor.Staff
        };

        if (cancellationActor == CancellationActor.Customer && rental.CustomerId != actor.UserId)
        {
            throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {id} was not found.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var paidAmount = await paymentGate.GetTotalPaidAmountAsync(id, ct);
        var rentAndVat = rental.TotalPrice; // rental rent + VAT

        var preview = CancellationPolicy.Evaluate(
            now: now,
            startTime: rental.ScheduledStart,
            currentStatus: rental.Status,
            paidAmount: paidAmount,
            rentAndVat: rentAndVat,
            actor: cancellationActor,
            staffInitiator: command.Initiator,
            waiveFee: command.WaiveFee,
            freeHoursBeforeStart: _cancelOptions.FreeHoursBeforeStart,
            lateFeePercent: _cancelOptions.LateFeePercent
        );

        if (!preview.Allowed)
        {
            throw new ConflictException("SELF_CANCEL_NOT_ALLOWED", string.Join("; ", preview.Reasons));
        }

        await repository.UpdateToCancelledAsync(
            id, rental.VehicleId, now, cancellationActor.ToString().ToLowerInvariant(), command.Reason, actor, ct);

        if (!rental.IsSubscription && paidAmount <= 0)
        {
            await paymentGate.VoidUnpaidInvoiceAsync(id, ct);
        }

        if (preview.RefundAmount > 0)
        {
            var primaryInvoiceId = await paymentGate.GetPrimaryInvoiceIdAsync(id, ct);
            await paymentGate.CreateRefundPaymentAsync(
                id, rental.CustomerId, primaryInvoiceId, preview.RefundAmount, rental.IsSubscription, ct);
        }

        await eventSink.PublishStatusChangedAsync(
            new RentalStatusChangedEvent(id, rental.Status, RentalStatus.Cancelled, now, actor), ct);

        return new RentalLifecycleResult(id, rental.Status.ToDbString(), RentalStatus.Cancelled.ToDbString(), "Rental cancelled successfully.", preview);
    }

    public async Task<CancellationPreview> GetCancellationPreviewAsync(int id, ActorContext actor, CancellationToken ct)
    {
        var rental = await repository.GetForUpdateAsync(id, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {id} was not found.");

        var cancellationActor = actor.Role switch
        {
            "Customer" => CancellationActor.Customer,
            "System" => CancellationActor.System,
            _ => CancellationActor.Staff
        };

        if (cancellationActor == CancellationActor.Customer && rental.CustomerId != actor.UserId)
        {
            throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {id} was not found.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var paidAmount = await paymentGate.GetTotalPaidAmountAsync(id, ct);
        var rentAndVat = rental.TotalPrice;

        return CancellationPolicy.Evaluate(
            now: now,
            startTime: rental.ScheduledStart,
            currentStatus: rental.Status,
            paidAmount: paidAmount,
            rentAndVat: rentAndVat,
            actor: cancellationActor,
            freeHoursBeforeStart: _cancelOptions.FreeHoursBeforeStart,
            lateFeePercent: _cancelOptions.LateFeePercent
        );
    }
}
