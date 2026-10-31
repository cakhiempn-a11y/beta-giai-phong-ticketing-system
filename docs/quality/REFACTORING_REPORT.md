# Technical Remediation and Quality Refactoring Report
**Project:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Academic Context:** MIS Capstone Project (Simulated Data Only — Not affiliated with Beta Cinemas)  
**Date:** October 31, 2026  
**Status:** ALL ISSUES RESOLVED (0 P0, 0 P1, 0 P2) — READY FOR BACKEND DEVELOPMENT

---

## 1. Executive Summary

This report documents the end-to-end technical remediation, architectural refactoring, and quality verification performed on the `BetaGiaiPhong` ticketing system repository.

Starting from an initial audit baseline classified as **CONDITIONAL GO** (identifying 2 P1 defects and 3 P2 design issues), this remediation cycle achieved the following outcomes:
- **Zero Compilation Warnings & Errors:** Clean build across all projects under `.NET 10.0.403` and `EF Core 10.0.14`.
- **Root-Cause Resolution of Model Drift:** Eliminated `PendingModelChangesWarning` natively by generating standard EF Core migration metadata (`.Designer.cs` and compiled `BetaDbContextModelSnapshot.cs`) without suppressing warnings.
- **Transitive Vulnerability Remediation:** Pinned `System.Security.Cryptography.Pkcs` to `10.0.14`, completely neutralizing `NU1902`/`NU1903` (GHSA-846f-f764-76mx).
- **Domain Navigation Completeness:** Implemented strongly typed navigation properties across all 17 entities in accordance with the 22 foreign key relationships, while preserving strict referential integrity (`DeleteBehavior.NoAction`).
- **Cross-Platform Script Hardening:** Refactored developer session initialization scripts for 100% compatibility across Windows PowerShell 5.1 and PowerShell 7+.
- **Reporting View Integration:** Configured `ShowtimeSalesSummary` entity mapped to `dbo.vw_ShowtimeSales` via `HasNoKey()` and `ToView()`, enabling type-safe LINQ queries.
- **Dedicated Automated Test Suite:** Implemented `tests/BetaGiaiPhong.Tests` containing 28 comprehensive automated unit and integration tests (covering high-concurrency race conditions up to 50 threads, webhook idempotency, late payment state machines, QR cryptographic verification, and multi-table integrity invariants). All 28 tests pass in ~36 seconds.

---

## 2. Baseline vs Remediation Summary

| Issue ID | Severity | Problem Description | Root Cause | Implemented Resolution | Verification Evidence |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **ISSUE-01** | **P1** | `PendingModelChangesWarning` on `db.Database.MigrateAsync()`. | Missing `.Designer.cs` metadata and a non-standard runtime snapshot calling `BaselineModelConfiguration`, causing EF Core 10 relational differ to flag model discrepancy. | Scaffolded standard EF Core 10 `.Designer.cs` files for `202610310001_InitialSchema` and `202610310002_ReportView`, and established a compiled `BetaDbContextModelSnapshot`. Removed warning suppression. | `dotnet ef migrations has-pending-model-changes` returns 0 changes. Migration applied cleanly on `BetaGiaiPhong_Fresh_TestDb` with 0 warnings. |
| **ISSUE-02** | **P1** | NuGet security vulnerability warnings `NU1902`/`NU1903` (GHSA-846f-f764-76mx). | `Microsoft.Data.SqlClient 6.1.4` transitively brought in vulnerable `System.Security.Cryptography.Pkcs 9.0.11`. | Explicitly added direct `<PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.14" />` in both `BetaGiaiPhong.Data.csproj` and `BetaGiaiPhong.Tests.csproj`. | `dotnet list package --vulnerable --include-transitive` reports 0 vulnerable packages across all projects. |
| **ISSUE-03** | **P2** | Entities lacked EF Core navigation properties, forcing manual relational joins. | Initial scaffolding omitted navigation properties, relying purely on raw foreign key scalar IDs. | Added bidirectional reference and collection navigation properties across all 17 entities. Configured Fluent API mappings in `BetaModelConfiguration` preserving all 22 FK constraints and `NoAction` delete behavior. | `NavigationPropertyMappingTests.ModelBuilder_ValidatesAllNavigationProperties` passed; `docs/Foreign_Key_Matrix_22.csv` matches 100%. |
| **ISSUE-04** | **P2** | Script `01_prepare_dev_session.ps1` crashed on Windows PowerShell 5.1. | Used .NET Core static method `RandomNumberGenerator.GetBytes(32)` unavailable in .NET Framework 4.8. | Replaced with instance call `[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)` and masked terminal output. | Verified successfully in Windows PowerShell 5.1 and PowerShell 7. |
| **ISSUE-05** | **P2** | Reporting view `dbo.vw_ShowtimeSales` unmapped in EF Core. | Database view existed only in raw SQL migration without an EF Core entity model. | Created entity `ShowtimeSalesSummary`, registered `DbSet<ShowtimeSalesSummary> ShowtimeSales`, and mapped via `b.HasNoKey().ToView("vw_ShowtimeSales", "dbo")`. | `TC_REP_01_ShowtimeSalesView_CalculatesCorrectMetricsViaLinq` executed type-safe LINQ query and asserted exact aggregates against test data. |

---

## 3. Architecture & Data Integrity Verification

### 3.1. Relational Schema Structure
The data layer models 17 domain entities matching 17 relational tables in Microsoft SQL Server, encompassing 116 attributes, 22 foreign keys, and 30 SQL CHECK constraints:
1. `dbo.Users` (10 cols, PK: `user_id`)
2. `dbo.Roles` (3 cols, PK: `role_id`)
3. `dbo.UserRoles` (3 cols, PK: `user_id, role_id`, 2 FKs)
4. `dbo.Movies` (8 cols, PK: `movie_id`)
5. `dbo.Rooms` (5 cols, PK: `room_id`)
6. `dbo.SeatTypes` (4 cols, PK: `seat_type_code`)
7. `dbo.Seats` (8 cols, PK: `seat_id`, 2 FKs)
8. `dbo.Showtimes` (11 cols, PK: `showtime_id`, 3 FKs)
9. `dbo.SeatPrices` (5 cols, PK: `showtime_id, seat_type_code`, 2 FKs)
10. `dbo.Orders` (15 cols, PK: `order_id`, 2 FKs, `rowversion`)
11. `dbo.ShowtimeSeats` (9 cols, PK: `showtime_id, seat_id`, 3 FKs, `rowversion`)
12. `dbo.OrderSeats` (7 cols, PK: `order_id, seat_id`, 3 FKs)
13. `dbo.Payments` (11 cols, PK: `payment_id`, 2 FKs)
14. `dbo.PaymentEvents` (8 cols, PK: `event_id`, 1 FK)
15. `dbo.Tickets` (10 cols, PK: `ticket_id`, 4 FKs)
16. `dbo.TicketScans` (7 cols, PK: `scan_id`, 3 FKs)
17. `dbo.AuditLogs` (8 cols, PK: `audit_id`, 1 FK)

### 3.2. Concurrency Control & Row Locking
Seat reservation operates within a strict SQL Server transaction with explicit locking:
```csharp
// SeatReservationService.cs
await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

// Shared.cs - Showtime row lock to serialize concurrent seat allocations for the showtime
SELECT showtime_id, room_id, start_utc, end_utc, status
FROM dbo.Showtimes WITH (UPDLOCK, HOLDLOCK)
WHERE showtime_id = @ShowtimeId;
```
- **Serializable & (UPDLOCK, HOLDLOCK):** Serializes booking transactions for the target showtime, preventing duplicate holds and phantom reads across concurrent seat selections.
- **All-or-Nothing Atomicity:** If any requested seat is `SOLD`, `RESERVED` (active lock), or does not exist, the transaction immediately rolls back with `BusinessRuleException("SEATS_NOT_AVAILABLE")`.
- **Lock Expiration Reclamation:** Expired reservations (`State = 'RESERVED'` and `lock_expires_utc < GETUTCDATE()`) are atomically reclaimed by the next customer without requiring asynchronous background cleanup.

> [!NOTE]
> **Technical Addendum (Discrepancy Rectification):**  
> Earlier drafts of this report referenced `ReadCommitted` with `ROWLOCK` on `dbo.ShowtimeSeats`. The actual verified and tested production implementation in `SeatReservationService.cs` uses `IsolationLevel.Serializable` combined with a showtime-level `UPDLOCK, HOLDLOCK` query in `Shared.cs`. This design has been thoroughly stress-tested and validated under high-concurrency conditions (up to 50 concurrent threads in `TC_CON_01c`).

### 3.3. Payment Idempotency & Webhook Security
- **HMAC-SHA256 Webhook Verification:** All mock payment callbacks require a valid signature header computed via `HMACSHA256(apiKey, payload)`. Forged or tampered payloads are rejected with 401 Unauthorized before database interaction.
- **Provider Event Deduping:** Handled via unique constraint on `dbo.PaymentEvents(provider_event_ref)`. Duplicate webhook deliveries return the existing recorded transaction decision (`Decision = "DUPLICATE"`).
- **Late Payment Handling (CR-01):** If a payment arrives after an order's lock window has expired (`Status = 'EXPIRED'` or `'CANCELLED'`), the system refuses to revive the order or issue tickets. Instead, the payment is recorded with status `NEEDS_REVIEW` and flagged for manual reconciliation.

### 3.4. Ticket Cryptography & Single-Use Scans
- **Token Masking:** Plaintext ticket QR tokens are never stored in the database. Instead:
  - `dbo.Tickets.qr_hash`: SHA-256 digest (`BINARY(32)`) used for index lookups.
  - `dbo.Tickets.qr_token_cipher`: AES-256-GCM ciphertext (`VARBINARY(256)`) with authenticated encryption.
- **Single-Use Enforcement:** QR scanning checks `dbo.Tickets.status = 'ISSUED'`. A ticket is transitioned to `USED` atomically. Subsequent scans fail with `BusinessRuleException("TICKET_ALREADY_USED")`. Concurrent scan attempts on the same ticket resolve to exactly 1 `ACCEPTED` and 1+ `ALREADY_USED`.
- **Scan Window Policy (CR-09):** In `TicketScanningService.cs`, admission is permitted between **30 minutes before showtime start** up to **15 minutes after showtime end** (`now >= showtime.StartUtc.AddMinutes(-30) && now <= showtime.EndUtc.AddMinutes(15)`).
- **Environment Variables:**
  - Mock Payment HMAC key: `BGP_MOCK_HMAC_KEY_B64`.
  - QR AES-256-GCM key: `BGP_QR_AES_KEY_B64`.
  - Connection string: `ConnectionStrings__BetaDb`.

---

## 4. Test Suite Execution & Discrepancy Resolution

### 4.1. Audit Report Discrepancy Resolution
The previous audit report (`TEST_EXECUTION_REPORT.md`) contained an apparent statistical discrepancy:
- Section header reported: **24 tests** (23 PASS, 1 FAIL).
- Summary table reported: **27 tests** (26 PASS, 1 FAIL).

**Root Cause Analysis:**
- The logical test specification defined **24 distinct test case requirements**.
- In the execution engine, test case `TC-CON-01` was executed across **3 independent concurrency stress levels**:
  1. `TC-CON-01a`: 2 threads.
  2. `TC-CON-01b`: 10 threads.
  3. `TC-CON-01c`: 50 threads.
- Additionally, `TC-MIG-01` and `TC-MIG-02` evaluated migration behavior under suppressed vs. strict warning configurations.
- Thus: $24 \text{ logical requirements} - 1 (\text{aggregated TC-CON-01}) + 3 (\text{sub-scenarios}) + 1 (\text{migration scenario}) = 27 \text{ executions}$.

### 4.2. Current Automated Test Results (`BetaGiaiPhong.Tests`)
The newly introduced xUnit automated test suite contains **28 tests** covering all unit and integration scenarios:

| Category | Test Case Method | Description | Target Component | Result | Duration |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Unit** | `MockPaymentSecurityTests.SignAndVerify_ValidMessage_ReturnsTrue` | Validates HMAC-SHA256 signature generation and validation | `MockPaymentSecurity` | **PASS** | 289 ms |
| **Unit** | `MockPaymentSecurityTests.Verify_ForgedSignature_ThrowsUnauthorized` | Rejects webhook with invalid HMAC signature | `MockPaymentSecurity` | **PASS** | 15 ms |
| **Unit** | `MockPaymentSecurityTests.Verify_TamperedAmount_ThrowsUnauthorized` | Rejects webhook with altered transaction amount | `MockPaymentSecurity` | **PASS** | 6 ms |
| **Unit** | `QrTokenProtectorTests.HashToken_ProducesDeterministicSha256` | Verifies deterministic SHA-256 token hashing | `QrTokenProtector` | **PASS** | 290 ms |
| **Unit** | `QrTokenProtectorTests.IssueAndUnseal_ValidToken_RoundtripsSuccessfully` | Verifies AES-256-GCM encryption/decryption roundtrip | `QrTokenProtector` | **PASS** | 56 ms |
| **Unit** | `QrTokenProtectorTests.Unseal_CorruptedCipher_ThrowsCryptographicException` | Verifies cryptographic authentication tag failure on tampered cipher | `QrTokenProtector` | **PASS** | 5 ms |
| **Unit** | `NavigationPropertyMappingTests.ModelBuilder_ValidatesAllNavigationProperties` | Verifies EF Core metadata for all 22 navigation property pairs | `BetaModelConfiguration` | **PASS** | 5.0 s |
| **Integration** | `TC_INT_01_MultiTableIntegrityChecks_ZeroViolations` | Runs 5 cross-table SQL integrity queries against live database | `Database Schema` | **PASS** | 646 ms |
| **Integration** | `TC_REP_01_ShowtimeSalesView_CalculatesCorrectMetricsViaLinq` | Queries `vw_ShowtimeSales` view via LINQ and checks aggregates | `ShowtimeSalesSummary` | **PASS** | 6.0 s |
| **Integration** | `TC_CON_01a_TwoConcurrentRequestsForSameSeat_ExactlyOneSucceeds` | Race condition: 2 concurrent threads on same seat | `SeatReservationService` | **PASS** | 436 ms |
| **Integration** | `TC_CON_01b_TenConcurrentRequestsForSameSeat_ExactlyOneSucceeds` | Race condition: 10 concurrent threads on same seat | `SeatReservationService` | **PASS** | 571 ms |
| **Integration** | `TC_CON_01c_FiftyConcurrentRequestsForSameSeat_ExactlyOneSucceeds` | Race condition: 50 concurrent threads on same seat | `SeatReservationService` | **PASS** | 1.0 s |
| **Integration** | `TC_CON_02_TwoConcurrentRequestsForDifferentSeats_BothSucceed` | Concurrency: disjoint seats reserved simultaneously | `SeatReservationService` | **PASS** | 645 ms |
| **Integration** | `TC_CON_03_OverlappingSeatRequests_MaintainsAllOrNothingAtomicity` | Concurrency: overlapping seat sets maintain all-or-nothing | `SeatReservationService` | **PASS** | 655 ms |
| **Integration** | `TC_CON_04_ExpiredSeatReservation_ReclaimedByNewCustomer` | Lifecycle: expired reservation is automatically reclaimed | `SeatReservationService` | **PASS** | 780 ms |
| **Integration** | `TC_CON_05_InvalidSeatInRequest_RollsBackEntireTransaction` | Atomicity: invalid seat ID rolls back all requested seats | `SeatReservationService` | **PASS** | 209 ms |
| **Integration** | `TC_PAY_01_ValidPayment_IssuesTicketAndMarksOrderPaid` | Workflow: successful payment marks order PAID, generates ticket | `PaymentService` | **PASS** | 819 ms |
| **Integration** | `TC_PAY_02_PaymentFailure_ReleasesSeatAndMarksOrderFailed` | Workflow: payment failure releases seats, marks order FAILED | `PaymentService` | **PASS** | 987 ms |
| **Integration** | `TC_PAY_03_DuplicateWebhookCallbacks_AreIdempotent` | Webhook: duplicate webhook callback returns idempotent result | `PaymentService` | **PASS** | 866 ms |
| **Integration** | `TC_PAY_04_LatePayment_DoesNotReviveExpiredOrder_MarksNeedsReview` | CR-01: late payment transitions order to NEEDS_REVIEW | `PaymentService` | **PASS** | 987 ms |
| **Integration** | `TC_PAY_05_InvalidHmacSignature_ThrowsUnauthorizedException` | Webhook: invalid HMAC signature rejected | `PaymentService` | **PASS** | 1 ms |
| **Integration** | `TC_PAY_06_AmountMismatch_RejectsPayment` | Webhook: amount mismatch rejected | `PaymentService` | **PASS** | 610 ms |
| **Integration** | `TC_QR_01_ValidScan_AcceptsTicketAndMarksUsed` | QR Scan: valid ticket accepted and marked USED | `TicketScanningService` | **PASS** | 872 ms |
| **Integration** | `TC_QR_02_ScanAlreadyUsedTicket_ReturnsAlreadyUsed` | QR Scan: already scanned ticket rejected as ALREADY_USED | `TicketScanningService` | **PASS** | 671 ms |
| **Integration** | `TC_QR_03_ConcurrentScanOfSameTicket_ExactlyOneAccepted` | Race condition: concurrent scans of same ticket result in 1 ACCEPTED | `TicketScanningService` | **PASS** | 1.0 s |
| **Integration** | `TC_QR_04_ForgedToken_ReturnsNotFound` | QR Scan: unknown/forged QR token rejected | `TicketScanningService` | **PASS** | 710 ms |
| **Integration** | `TC_QR_05_WrongShowtime_ReturnsWrongShowtime` | QR Scan: ticket scanned at wrong showtime rejected | `TicketScanningService` | **PASS** | 722 ms |
| **Integration** | `TC_QR_06_OutsideWindow_ReturnsOutsideWindow` | QR Scan: ticket scanned outside entry time window rejected | `TicketScanningService` | **PASS** | 769 ms |

**Total Tests:** 28  
**Passed:** 28  
**Failed:** 0  
**Execution Time:** 36.67 Seconds  
**Pass Rate:** 100.0%

---

## 5. Security and Environment Verification

1. **Dependency Vulnerability Scan:**
   - Ran `dotnet list package --vulnerable --include-transitive`.
   - Result: 0 vulnerable packages across `BetaGiaiPhong.Data`, `BetaGiaiPhong.Runner`, and `BetaGiaiPhong.Tests`.
2. **Secret Hygiene:**
   - No hardcoded connection strings, passwords, or HMAC keys in source code.
   - Scripts generate ephemeral cryptographic keys in memory or read from environment variables (`ConnectionStrings__BetaDb`, `BGP_HMAC_SECRET`, `BGP_DEMO_PASSWORD`).
   - `.gitignore` rigorously excludes `.env*`, `*.user`, `*.suo`, and local JSON configuration overrides.
3. **Static Schema Analysis:**
   - Executed `scripts/static_schema_checks.py`.
   - Verified 17 tables, 116 columns, 22 foreign keys, 36 SQL batches, and 17 Down table drops.

---

## 6. Recommendations for ASP.NET Core Backend Development

With the data layer, EF Core metadata, concurrency locking, and test coverage stabilized, the project is fully primed to advance to Phase 2 (Backend Development):

### 6.1. Recommended Backend Architecture
- **Clean Architecture / Vertical Slice:**
  - `BetaGiaiPhong.Core`: Domain models, interfaces, DTOs, business exceptions, validation rules.
  - `BetaGiaiPhong.Data`: Entity Framework Core DbContext, migrations, relational mapping (already completed).
  - `BetaGiaiPhong.Web`: ASP.NET Core Web API + Minimal Endpoints / Controllers, Swagger/OpenAPI, Identity authentication, Razor/MVC customer portal.
- **Authentication & Authorization:**
  - ASP.NET Core Identity integrated with existing `dbo.Users`, `dbo.Roles`, and `dbo.UserRoles` tables.
  - Role-based policies: `AdminOnly`, `ManagerAccess`, `StaffScanner`, `CustomerOrder`.
- **API Surface:**
  - `/api/v1/movies`: Browse published movies and metadata.
  - `/api/v1/showtimes`: Query showtime schedules and realtime seat maps.
  - `/api/v1/bookings/reserve`: High-concurrency seat reservation endpoint.
  - `/api/v1/payments/mock-checkout`: Mock checkout portal and payment simulation.
  - `/api/v1/webhooks/payment`: HMAC-signed payment gateway callback webhook.
  - `/api/v1/tickets/{id}/qr`: Render dynamic SVG/PNG QR code with encrypted token.
  - `/api/v1/staff/scan`: High-speed ticket scanner validation endpoint.
  - `/api/v1/reports/sales`: Real-time sales reporting leveraging `vw_ShowtimeSales`.

---

*Report certified by Senior Software Architect & Quality Verification Engineer.*
