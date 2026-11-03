-- Phase 2 / D2 (follow-up): the supplementary invoice of a subscription rental must be COLLECTABLE.
--
-- 0033 made the excess overtime of a subscription rental a real 'supplementary' invoice. It could not be
-- paid, though: fn_check_payment_matches_rental required payments.is_subscription_payment to equal
-- rentals.is_subscription, and a cash/PayOS receipt is NOT paid by the package, so every invoice-linked
-- payment on a subscription rental was rejected by the trigger after the money had been taken.
--
-- This migration opens exactly that one path and nothing else:
--   1. fn_check_payment_matches_rental gains three changes, in this order:
--      (i)   an invoice-linked payment must belong to the same rental as its invoice - before any early
--            return. This closes a pre-existing hole: until now a payment could point at another rental's
--            invoice and only the application code stopped it.
--      (ii)  the plan-purchase early return stays, but AFTER (i), so a plan purchase (invoice_id NULL) still
--            returns immediately.
--      (iii) a 'supplementary' invoice skips the flag-versus-rental check entirely: it is paid in cash by the
--            customer, not by the package, so is_subscription_payment=false on a subscription rental is
--            legitimate. Every other kind keeps the old rule.
--   2. trg_payment_match_rental is recreated with invoice_id in its UPDATE OF list. Without that column an
--      UPDATE payments SET invoice_id = ... does not fire the trigger at all.
--   3. ck_payments_plan_purchase_shape: only a plan purchase may carry plan_id.
--   4. ck_payments_invoice_needs_rental: an invoice-linked payment must carry a rental_id. This is defence in
--      depth and NEITHER guard may be removed:
--        - trigger branch (i) rejects it first on the normal path, because it tests NEW.rental_id IS NULL
--          explicitly instead of relying on a NULL comparison (a NULL comparison is unknown, never false, so
--          a naive `invoice.rental_id <> NEW.rental_id` would fall through to the plan-purchase early return);
--        - the CHECK still holds when the trigger does not fire at all: BEFORE UPDATE OF only fires for the
--          listed columns, and a dropped or disabled trigger leaves the row completely unprotected.
--      Measured: with both in place the trigger raises P0001 first and the CHECK is never reached; disable the
--      trigger and the CHECK answers with 23514. Deleting the CHECK because "the trigger already handles it"
--      reopens the hole the moment the trigger changes.
--
-- Nothing here changes prices, refund rules or invoice amounts.
-- Idempotent: every statement is guarded, nothing destructive, safe to re-run.

-- 1. Invoice ownership first, then the plan-purchase early return, then the supplementary exemption -----
CREATE OR REPLACE FUNCTION fn_check_payment_matches_rental()
RETURNS TRIGGER AS $$
DECLARE
    v_is_sub             BOOLEAN;
    v_invoice_rental_id  INTEGER;
    v_invoice_kind       VARCHAR(20);
BEGIN
    -- (i) An invoice-linked payment must belong to the rental of its invoice. Written with explicit NULL
    --     checks because `invoice.rental_id <> NEW.rental_id` is UNKNOWN, not true, when either side is NULL.
    IF NEW.invoice_id IS NOT NULL THEN
        SELECT rental_id, kind INTO v_invoice_rental_id, v_invoice_kind
          FROM invoices WHERE id = NEW.invoice_id;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'Invoice % không tồn tại', NEW.invoice_id;
        END IF;
        IF v_invoice_rental_id IS NULL
           OR NEW.rental_id IS NULL
           OR v_invoice_rental_id <> NEW.rental_id THEN
            RAISE EXCEPTION 'Payment gắn invoice % của rental %, không khớp rental % của payment',
                NEW.invoice_id, COALESCE(v_invoice_rental_id::TEXT, 'NULL'), COALESCE(NEW.rental_id::TEXT, 'NULL');
        END IF;
    END IF;

    -- (ii) Payment mua gói không gắn rental (plan_id có giá trị, invoice_id NULL) nên bỏ qua kiểm tra.
    --      This early return must stay BELOW (i): a plan purchase has no invoice to own.
    IF NEW.rental_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT is_subscription INTO v_is_sub FROM rentals WHERE id = NEW.rental_id;
    IF v_is_sub IS NULL THEN
        RAISE EXCEPTION 'Rental % không tồn tại', NEW.rental_id;
    END IF;

    -- (iii) D2: a supplementary invoice is paid by the customer in cash, not by the package, so its payment
    --       legitimately carries is_subscription_payment = false on a subscription rental. v_invoice_kind is
    --       NULL when invoice_id is NULL, and `NULL = 'supplementary'` is UNKNOWN, so every other kind falls
    --       through to the unchanged rule below.
    IF v_invoice_kind = 'supplementary' THEN
        RETURN NEW;
    END IF;

    IF v_is_sub <> NEW.is_subscription_payment THEN
        RAISE EXCEPTION 'is_subscription_payment không khớp với rental %', NEW.rental_id;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- 2. The trigger must fire when invoice_id changes, not only when rental_id/is_subscription_payment do ----
DROP TRIGGER IF EXISTS trg_payment_match_rental ON payments;
CREATE TRIGGER trg_payment_match_rental
    BEFORE INSERT OR UPDATE OF rental_id, is_subscription_payment, invoice_id ON payments
    FOR EACH ROW EXECUTE FUNCTION fn_check_payment_matches_rental();

-- 3. A plan purchase is the only thing allowed to carry plan_id ---------------------------------------
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
     WHERE conname = 'ck_payments_plan_purchase_shape'
       AND conrelid = 'payments'::regclass
  ) THEN
    ALTER TABLE payments ADD CONSTRAINT ck_payments_plan_purchase_shape
      CHECK (plan_id IS NULL OR (is_subscription_payment AND invoice_id IS NULL AND rental_id IS NULL))
      NOT VALID;
  END IF;
END $$;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint
     WHERE conname = 'ck_payments_plan_purchase_shape'
       AND conrelid = 'payments'::regclass
       AND NOT convalidated
  ) THEN
    ALTER TABLE payments VALIDATE CONSTRAINT ck_payments_plan_purchase_shape;
  END IF;
END $$;

-- 4. An invoice-linked payment must carry a rental_id (the NULL-rental hole) ---------------------------
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
     WHERE conname = 'ck_payments_invoice_needs_rental'
       AND conrelid = 'payments'::regclass
  ) THEN
    ALTER TABLE payments ADD CONSTRAINT ck_payments_invoice_needs_rental
      CHECK (invoice_id IS NULL OR rental_id IS NOT NULL)
      NOT VALID;
  END IF;
END $$;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint
     WHERE conname = 'ck_payments_invoice_needs_rental'
       AND conrelid = 'payments'::regclass
       AND NOT convalidated
  ) THEN
    ALTER TABLE payments VALIDATE CONSTRAINT ck_payments_invoice_needs_rental;
  END IF;
END $$;

COMMENT ON CONSTRAINT ck_payments_plan_purchase_shape ON payments IS
  'Phase 2 D2: only a plan purchase (subscription paid online) may set plan_id; it carries no invoice and no rental.';
COMMENT ON CONSTRAINT ck_payments_invoice_needs_rental ON payments IS
  'Phase 2 D2: an invoice-linked payment must carry a rental_id. Independent backstop for the trigger branch that rejects the same shape - keep both.';
