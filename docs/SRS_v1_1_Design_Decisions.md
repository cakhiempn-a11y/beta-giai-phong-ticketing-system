# Proposed changes to SRS v1.1 (not yet formally approved)

| ID | Change | Rationale |
|---|---|---|
| CR-01 | Late payment: preserve terminal order (`EXPIRED`, `CANCELLED`); mark `Payments.status=NEEDS_REVIEW`, add `PaymentEvents` record. Never issue a conflicting ticket. | Maintain audit and unique sold seat invariant. |
| CR-02 | Showtime room/seat validity: use `(showtime_id, room_id)` and `(seat_id, room_id)` FKs to validate placement. | Prevent cross-room seat booking. |
| CR-03 | Seat holding default 5 min; all checks use server UTC; use short `SERIALIZABLE` transactions and explicit UPDATE locks on Showtime. | Prevent concurrent overselling, with DB as source of truth. |
| CR-04 | Payment callback must be verified server-side; use unique `(provider, transaction_ref)` and `(provider, event_ref)`. | Prevent duplicate issuance and duplicate event processing. |
| CR-05 | Store sale-price snapshot in OrderSeats; report totals from `PAID` orders only. | Preserve transaction history when prices change. |
| CR-06 | QR stores non-guessable token; DB stores SHA-256 hash and AES-256-GCM sealed representation, with key outside DB. | Avoid readable tokens in the database. |
| CR-07 | `Showtimes.sellable_capacity_snapshot` freezes the available sellable count for occupancy statistics. | Avoid historical denominator drift. |
| CR-08 | Scan window default: 30 min before start to 15 min after end (test policy, not from Beta). | Allow deterministic test cases. |
| CR-09 | No physical deletes for transactional references; use statuses; append audit records in the SAME transaction. | Traceability and referential integrity. |

## Unresolved before production
- Provider-specific payment settlement/refund/reconciliation; this demo ONLY has mock HMAC.
- Credentials and account lockout/activation/privacy (authentication UI not included).
- Secrets rotation/re-encryption and key management policy.
- Safe retries and deadlock error mapping for hostile production load.
- Cancellation policy for paid orders and pricing/promotion combination, out of P0.
