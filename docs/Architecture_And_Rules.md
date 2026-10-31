# Data model and transactional boundaries

## Database scope
Single-cinema prototype, **not** Beta Cinemas' actual production data. 17 logical tables / 116 mapped fields / 22 foreign keys, from the earlier ERD v1.0. No real payments are processed.

## Relationship clusters
- Accounts: Users, Roles, UserRoles.
- Film/showtime: Movies, Rooms, SeatTypes, Seats, Showtimes, SeatPrices.
- Booking: Orders, ShowtimeSeats, OrderSeats.
- Financial trail: Payments, PaymentEvents.
- Admissions: Tickets, TicketScans.
- Audit: AuditLogs.

## SQL Server baseline
- The `InitialSchema` EF migration is a manually translated baseline of the previous approved SQL DDL (split at `GO` into individual batches).
- `BetaDbContextModelSnapshot` includes a FROZEN copy of the baseline Fluent configuration, not a pointer to future model changes.
- Two migrations create the 17-table baseline and the `vw_ShowtimeSales` view. Migration **is authoritative for new empty databases**. `sql/01_reference_schema_SQL_ONLY.sql` is for review/comparison ONLY. Never run both as this causes object conflicts.
- For subsequent changes: update entities/configuration, run `dotnet ef migrations add <Name>` and review the generated SQL. Do not edit already applied migration scripts.

## Locking rules
1. Every operation that modifies seats/orders during a showtime holds a transaction with `IsolationLevel.Serializable`.
2. The transaction acquires a SHOWTIME row lock (`UPDLOCK,HOLDLOCK`) before it reads/modifies seat state or order state.
3. SHOWTIME publish checks overlaps after acquiring ROOM row lock.
4. A multi-seat order is all-or-nothing. The transaction rolls back if any selected seat is unavailable.
5. External payment execution MUST occur **outside** the database transaction. Only the authenticated callback is handled inside a brief DB transaction.
6. Ticket acceptance takes an exclusive-compatible ticket update lock and moves `ISSUED` to `USED` once.
7. Any direct SQL mutations outside services must preserve the same lock order and invariants; DB foreign keys alone do not enforce complete business rules.

## Key application/database conventions
- SQL Server `datetime2(3)` values represent **UTC**; SqlClient materializes DateTime with `Kind=Unspecified` by default, so API boundaries must normalize UTC.
- VND values are `BIGINT`, never float.
- `ShowtimeSeats` tracks the showtime/physical seat pair; `SOLD` is irreversible within P0.
- QR encryption key and mock webhook HMAC key are environment variables, not repository contents.
- PaymentEvents accepts duplicate attempt events as audit records; exact provider event IDs are unique.

## Known limitations
- Database engine and .NET SDK are not available in the generation environment; all SQL/EF model mapping checks are static until user executes local `dotnet build`, migration, smoke and race tests.
- A pessimistic showtime-wide lock minimizes race conditions in a single-cinema prototype but can reduce throughput for popular showtimes. A later optimized design can use conditional row updates with retry and per-seat locking.
- Long-lived DbContext instances are NOT intended for production; use request-scoped contexts in ASP.NET Core.
- Services are **reference implementations**, not a finished production payment/authentication system.
