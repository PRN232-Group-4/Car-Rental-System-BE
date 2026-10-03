-- BE-21: subscription-plan purchases are paid through PayOS without a rental or invoice.
-- A plan-purchase row keeps is_subscription_payment = TRUE and invoice_id = NULL (satisfying
-- ck_payments_invoice_rule) and stores the purchased plan in plan_id.
ALTER TABLE payments ALTER COLUMN rental_id DROP NOT NULL;
ALTER TABLE payments ADD COLUMN IF NOT EXISTS plan_id INTEGER; -- -> subscription_plans.id (NULL for rental/invoice payments)

-- Plan-purchase rows have rental_id NULL, so the rental/subscription consistency check must skip them.
CREATE OR REPLACE FUNCTION fn_check_payment_matches_rental()
RETURNS TRIGGER AS $$
DECLARE
    v_is_sub BOOLEAN;
BEGIN
    IF NEW.rental_id IS NULL THEN
        RETURN NEW;
    END IF;
    SELECT is_subscription INTO v_is_sub FROM rentals WHERE id = NEW.rental_id;
    IF v_is_sub IS NULL THEN
        RAISE EXCEPTION 'Rental % không tồn tại', NEW.rental_id;
    END IF;
    IF v_is_sub <> NEW.is_subscription_payment THEN
        RAISE EXCEPTION 'is_subscription_payment không khớp với rental %', NEW.rental_id;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
