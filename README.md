# Beta Giải Phóng — Online Cinema Ticketing System

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![EF Core 10](https://img.shields.io/badge/EF%20Core-10.0.14-512BD4?logo=dotnet)](https://learn.microsoft.com/ef/core/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-2025%20%7C%202022-CC292B?logo=microsoftsqlserver)](https://www.microsoft.com/sql-server/)
[![Build & Tests](https://img.shields.io/badge/Tests-95%20Passed%20%7C%200%20Failed-brightgreen)](tests/BetaGiaiPhong.Tests)
[![Security Scan](https://img.shields.io/badge/Vulnerabilities-0-brightgreen)](#7-security-architecture)

> **Academic Disclaimer:**  
> This software is an academic capstone prototype developed for the Management Information Systems (MIS) curriculum. It uses purely synthetic simulation data and is **not affiliated with, endorsed by, or integrated into the production systems or infrastructure of Beta Cinemas**. No real payment gateways or customer credentials are used.

---

## 1. Project Title & Academic Disclaimer

- **Project Title:** Analysis, Design, and Implementation of an Online Cinema Ticketing Management System for Beta Giải Phóng Cinema.
- **Context:** MIS Graduation Capstone Project.
- **Scope Boundary:** This repository encompasses the full backend ticketing management system: the foundational **Data Layer, Entity Framework Core Relational Modeling, Transactional Services, and Concurrency Verification Suite**, alongside the **ASP.NET Core Web Application** providing Cookie Authentication, Role Authorization, Public Catalog, Interactive Seat Map, Transactional Reservation (BE-01/BE-02), and **Mock Payment, Secure HMAC Webhooks, Electronic Tickets & QR Code Generation (BE-03)**. Real payment gateways and physical turnstile hardware are simulated using academic cryptographic contracts.

---

## 2. Architectural Overview

The solution adheres to clean data-driven and modular monolith design patterns:
```
                                ┌───────────────────────────────────────┐
                                │          BetaGiaiPhong.Web            │
                                │  (ASP.NET Core MVC & RESTful API,     │
                                │   Auth, Catalog, Booking, Checkout,   │
                                │   Mock Webhook, Electronic Tickets)   │
                                └──────────────────┬────────────────────┘
                                                   │ references
                                                   ▼
┌───────────────────────────────────────┐       ┌───────────────────────────────────────┐
│        BetaGiaiPhong.Runner           │       │         BetaGiaiPhong.Data            │
│  (CLI Migration, Seed & Verification) ├──────►│  (Entities, DbContext, Migrations,    │
└───────────────────────────────────────┘       │   Domain Services, Cryptography)      │
                                                └──────────────────▲────────────────────┘
                                                                   │ references
                                                ┌──────────────────┴────────────────────┐
                                                │         BetaGiaiPhong.Tests           │
                                                │  (xUnit: Unit, Integration & Web)     │
                                                └───────────────────────────────────────┘
```

- **`BetaGiaiPhong.Web`:** ASP.NET Core host providing server-rendered Razor Views and RESTful JSON APIs for public catalog exploration (published movies, showtimes grouped by date, and real-time interactive seat maps), transactional seat reservation (1–8 seats with 5-minute hold), order lifecycle management, mock payment checkout with HMAC-SHA256 signature verification, electronic ticket issuance, and dynamic QR code generation. Implements secure Cookie Authentication and role authorization (`CUSTOMER`, `STAFF`, `MANAGER`, `ADMIN`) mapped to `dbo.Users` without extra Identity tables.
- **`BetaGiaiPhong.Data`:** Contains 17 domain entities, `BetaDbContext`, Fluent API mappings (`BetaModelConfiguration`), EF Core Migrations, and core domain services (`SeatReservationService`, `PaymentService`, `TicketScanningService`, `OrderLifecycleService`, `ShowtimePublishingService`).
- **`BetaGiaiPhong.Runner`:** Administrative CLI tool for applying database migrations, seeding synthetic demo data, and executing integration validation scenarios. Directly references `BetaGiaiPhong.Data`.
- **`BetaGiaiPhong.Tests`:** Comprehensive automated xUnit test suite (95 tests) covering unit tests, live SQL Server integration scenarios (high-thread concurrency, webhook idempotency, QR cryptographic verification, database integrity invariants), and Web application integration tests via `WebApplicationFactory`.

---

## 3. Technology Stack & Prerequisites

- **SDK:** .NET 10 SDK (tested on `.NET 10.0.403`).
- **Framework & ORM:** Entity Framework Core 10 (`10.0.14`).
- **Database Engine:** Microsoft SQL Server (SQL Server 2022 / 2025 Developer Edition or Express).
- **Tooling:** EF Core CLI Tools (`dotnet-ef 10.0.14`), PowerShell 5.1 / 7+, Docker Desktop (optional, for containerized SQL Server).

---

## 4. Repository Structure

```text
BetaGiaiPhong/
├── .editorconfig                          # Global code style and formatting standards
├── .gitignore                             # Comprehensive Git exclusion rules
├── BetaGiaiPhong.slnx                     # Visual Studio XML solution definition
├── docker-compose.yml                     # Local SQL Server 2022 Developer container
├── dotnet-tools.json                      # Local .NET tools manifest (dotnet-ef 10.0.14)
├── global.json                            # .NET SDK version configuration
├── README.md                              # Main technical documentation
├── docs/                                  # Architectural specifications & data models
│   ├── Architecture_And_Rules.md          # Architectural decisions & concurrency constraints
│   ├── BGP_TaiLieu_EFCore_SQLServer_v1_0.docx
│   ├── Data_Dictionary_116_Columns.csv    # 116-attribute relational data dictionary
│   ├── ERD_Source_Editable.drawio         # Editable entity-relationship diagram source
│   ├── Foreign_Key_Matrix_22.csv          # 22-relationship referential integrity matrix
│   ├── model_manifest.json                # JSON schema manifest for static validation
│   ├── SRS_v1_1_Design_Decisions.md       # Business requirements and design rationale
│   ├── audit/                             # Baseline audit reports from previous reviews
│   │   ├── AUDIT_ISSUES.md
│   │   ├── AUDIT_REPORT.md
│   │   ├── BACKEND_READINESS.md
│   │   ├── SRS_TRACEABILITY_AUDIT.md
│   │   └── TEST_EXECUTION_REPORT.md
│   ├── figures/                           # Visual ERD diagrams
│   │   ├── ERD_Ghe_Don_Ve_v1.png
│   │   └── ERD_TongQuan_v1.png
│   └── quality/                           # Remediation & quality validation reports
│       ├── BE_01_REPORT.md
│       └── REFACTORING_REPORT.md
├── references/                            # Academic inception documents & UML specs
│   ├── Beta_Giai_Phong_BPMN_TO_BE_v1_0.docx
│   ├── Beta_Giai_Phong_De_Cuong_Nghien_Cuu.docx
│   ├── Beta_Giai_Phong_SRS_v1_0.docx
│   ├── BGP_Sequence_Diagrams_ThuyetMinh_v1_0.docx
│   └── BGP_UseCase_TongQuat_Va_DacTa_ChiTiet_v1_0.docx
├── scripts/                               # Cross-platform developer automation scripts
│   ├── 01_prepare_dev_session.ps1         # Environment initialization & ephemeral secrets
│   ├── 02_run_dev.ps1                     # Local pipeline execution script
│   └── static_schema_checks.py            # Static DDL & code token invariant checks
├── sql/                                   # Reference DDL and verification SQL scripts
│   ├── 01_reference_schema_SQL_ONLY.sql   # Baseline 17-table relational schema
│   ├── 02_seed_reference_data.sql         # Seed catalog reference data
│   ├── 03_report_view.sql                 # Sales report view definition
│   └── 04_integrity_checks.sql            # Cross-table multi-invariant integrity checks
├── src/
│   ├── BetaGiaiPhong.Data/                # EF Core 10 data layer & domain services
│   ├── BetaGiaiPhong.Runner/              # Administrative CLI console application
│   └── BetaGiaiPhong.Web/                 # ASP.NET Core MVC & RESTful API host
└── tests/
    └── BetaGiaiPhong.Tests/               # xUnit unit, integration & web test suite
```

---

## 5. Database Schema & ERD Summary

The relational database architecture is defined across **17 domain entities**, **116 attributes**, **22 foreign keys**, and **30 SQL CHECK constraints**:

| Entity | Relational Table | Primary Key | Description |
| :--- | :--- | :--- | :--- |
| `AppUser` | `dbo.Users` | `user_id` (`BIGINT`) | System users (customers, staff, managers, admins) |
| `Role` | `dbo.Roles` | `role_id` (`INT`) | Authorization roles |
| `UserRole` | `dbo.UserRoles` | `user_id, role_id` | User-to-role junction table |
| `Movie` | `dbo.Movies` | `movie_id` (`BIGINT`) | Films and screening metadata |
| `Room` | `dbo.Rooms` | `room_id` (`INT`) | Cinema auditoriums |
| `SeatType` | `dbo.SeatTypes` | `seat_type_code` (`VARCHAR(16)`) | Physical seat tiers (`STANDARD`, `VIP`, `COUPLE`) |
| `Seat` | `dbo.Seats` | `seat_id` (`BIGINT`) | Physical seats in rooms |
| `Showtime` | `dbo.Showtimes` | `showtime_id` (`BIGINT`) | Scheduled screenings |
| `SeatPrice` | `dbo.SeatPrices` | `showtime_id, seat_type_code` | Showtime-specific price list |
| `BookingOrder` | `dbo.Orders` | `order_id` (`BIGINT`) | Customer booking orders (`rowversion`) |
| `ShowtimeSeat` | `dbo.ShowtimeSeats` | `showtime_id, seat_id` | Real-time seat inventory states (`rowversion`) |
| `OrderSeat` | `dbo.OrderSeats` | `order_id, seat_id` | Immutable seat snapshots for orders |
| `Payment` | `dbo.Payments` | `payment_id` (`BIGINT`) | Payment attempts |
| `PaymentEvent` | `dbo.PaymentEvents` | `event_id` (`BIGINT`) | Webhook audit trail (idempotent reference key) |
| `Ticket` | `dbo.Tickets` | `ticket_id` (`BIGINT`) | Issued entrance tickets with hashed/encrypted QR |
| `TicketScan` | `dbo.TicketScans` | `scan_id` (`BIGINT`) | Entrance scanning audit logs |
| `AuditLog` | `dbo.AuditLogs` | `audit_id` (`BIGINT`) | Administrative audit trail |
| `ShowtimeSalesSummary` | `dbo.vw_ShowtimeSales` | Keyless View (`HasNoKey()`) | Aggregated sales metrics view |

---

## 6. Business Rules & Concurrency Control (CR-01 to CR-09)

The data layer enforces core operational invariants:
- **CR-01 (Late Payment Handling):** When a payment callback arrives after the booking order has expired (`Status = 'EXPIRED'` or `'CANCELLED'`), the system refuses to revive the order or issue tickets. The payment is recorded with `Status = 'NEEDS_REVIEW'` for manual administrative reconciliation.
- **CR-00 (Order Code Specification):** Order codes are generated strictly in the format `"BGP-" + Guid.NewGuid().ToString("N").ToUpperInvariant()`, yielding a high-entropy 36-character non-sequential identifier resistant to enumeration attacks.
- **CR-02 (Seat Reservation Limit):** A single order may contain at most 8 seats.
- **CR-03 (Lock Duration):** Default seat hold duration is 5 minutes from reservation time.
- **CR-04 (Pessimistic Row Locking):** Seat reservation executes under `IsolationLevel.Serializable` with an explicit showtime lock (`SELECT ... FROM dbo.Showtimes WITH (UPDLOCK, HOLDLOCK)`) to serialize concurrent seat allocations for the showtime and guarantee strict all-or-nothing atomicity.
- **CR-05 (All-or-Nothing Atomicity):** If any requested seat is unavailable, the entire reservation transaction rolls back.
- **CR-06 (Price Immutability):** Unit prices are captured in `dbo.OrderSeats` at reservation time and never updated even if showtime price tiers change.
- **CR-07 (Webhook Idempotency):** Duplicate payment webhook deliveries are acknowledged without re-processing or duplicate ticket generation.
- **CR-08 (QR Single-Use):** Ticket tokens can be redeemed exactly once. Concurrent scans result in at most one `ACCEPTED` outcome.
- **CR-09 (Scan Time Window):** Tickets may only be scanned within a designated time window relative to showtime start (from 30 minutes before showtime start up to 15 minutes after showtime end).

---

## 7. Security Architecture

- **Dependency Hygiene:** `System.Security.Cryptography.Pkcs` is explicitly pinned to version `10.0.14`, completely resolving `NU1902`/`NU1903` vulnerabilities (GHSA-846f-f764-76mx).
- **Authentication & Authorization:** Modular cookie authentication leveraging existing `dbo.Users` with PBKDF2 (`PasswordHasher<AppUser>`), role-based policies (`CUSTOMER`, `STAFF`, `MANAGER`, `ADMIN`), login rate limiting, and sliding session expiration.
- **Anti-Forgery & Web Security:** Anti-CSRF protection across all forms (`ValidateAntiForgeryToken`), `SameSite=Lax` cookies, HTTP security headers (`nosniff`, `DENY` framing, `strict-origin-when-cross-origin`), and correlation ID tracing (`X-Correlation-ID`).
- **Webhook Authentication:** Webhooks are authenticated via `HMAC-SHA256` signatures verified using constant-time comparison against an application secret.
- **QR Token Cryptography:**
  - Plaintext tokens are never stored in the database.
  - Indexing: SHA-256 digest stored in `dbo.Tickets.qr_hash`.
  - Storage: Authenticated AES-256-GCM ciphertext in `dbo.Tickets.qr_token_cipher`.
- **Secret Management:** Connection strings and cryptographic secrets are supplied exclusively via environment variables (`ConnectionStrings__BetaDb`, `BGP_MOCK_HMAC_KEY_B64`, `BGP_QR_AES_KEY_B64`, `BGP_DEMO_PASSWORD`). No production or development credentials are committed.

---

## 8. EF Core Migrations

The repository maintains an authoritative, drift-free EF Core 10 migration history:
- `202610310001_InitialSchema`: 17 relational tables, primary keys, foreign keys, CHECK constraints, and unique filtered indexes.
- `202610310002_ReportView`: Creation of `dbo.vw_ShowtimeSales` view.
- `BetaDbContextModelSnapshot`: Complete, compiled relational snapshot aligning 1:1 with the database schema.

**Zero Model Drift:**
```powershell
dotnet ef migrations has-pending-model-changes --project .\src\BetaGiaiPhong.Data --startup-project .\src\BetaGiaiPhong.Data
# Output: No changes have been made to the model since the last migration.
```

---

## 9. Local Development Setup

### 9.1. Environment Configuration
Execute `scripts/01_prepare_dev_session.ps1` in Windows PowerShell 5.1 or PowerShell 7+:
```powershell
# Set your local SQL Server connection string
$env:ConnectionStrings__BetaDb = "Server=localhost;Database=BetaGiaiPhong_Dev;Trusted_Connection=True;TrustServerCertificate=True;"
$env:BGP_ALLOW_DEMO_SEED = "true"
$env:BGP_DEMO_PASSWORD = "DemoSecurePassword123!"

# Run session preparation (generates ephemeral HMAC & AES keys in memory)
.\scripts\01_prepare_dev_session.ps1
```

### 9.2. Docker Compose (Alternative)
For isolated containerized SQL Server development:
```powershell
$env:BGP_SQL_SA_PASSWORD = "StrongLocalDevPassword123!"
docker compose up -d
```

---

## 10. Database Initialization & Seeding

Run administrative actions using `BetaGiaiPhong.Runner`:
```powershell
# Apply EF Core migrations
dotnet run --project .\src\BetaGiaiPhong.Runner -- migrate

# Inspect applied migration status
dotnet run --project .\src\BetaGiaiPhong.Runner -- status

# Seed synthetic reference catalog & demo users
dotnet run --project .\src\BetaGiaiPhong.Runner -- seed-demo

# Execute end-to-end smoke verification
dotnet run --project .\src\BetaGiaiPhong.Runner -- smoke

# Run multi-table integrity invariants check
dotnet run --project .\src\BetaGiaiPhong.Runner -- verify
```

---

## 11. Running Automated Tests

The solution features a dedicated xUnit test suite (`tests/BetaGiaiPhong.Tests`):
```powershell
# Run all unit and integration tests
dotnet test .\BetaGiaiPhong.slnx --logger "console;verbosity=normal"
```

### Test Suite Execution Output
```text
Passed BetaGiaiPhong.Tests.UnitTests.MockPaymentSecurityTests.SignAndVerify_ValidMessage_ReturnsTrue
Passed BetaGiaiPhong.Tests.UnitTests.QrTokenProtectorTests.HashToken_ProducesDeterministicSha256
Passed BetaGiaiPhong.Tests.UnitTests.MockPaymentSecurityTests.Verify_ForgedSignature_ThrowsUnauthorized
Passed BetaGiaiPhong.Tests.UnitTests.MockPaymentSecurityTests.Verify_TamperedAmount_ThrowsUnauthorized
Passed BetaGiaiPhong.Tests.UnitTests.QrTokenProtectorTests.IssueAndUnseal_ValidToken_RoundtripsSuccessfully
Passed BetaGiaiPhong.Tests.UnitTests.QrTokenProtectorTests.Unseal_CorruptedCipher_ThrowsCryptographicException
Passed BetaGiaiPhong.Tests.UnitTests.NavigationPropertyMappingTests.ModelBuilder_ValidatesAllNavigationProperties
Passed BetaGiaiPhong.Tests.IntegrationTests.ReportingAndIntegrityTests.TC_INT_01_MultiTableIntegrityChecks_ZeroViolations
Passed BetaGiaiPhong.Tests.IntegrationTests.ReportingAndIntegrityTests.TC_REP_01_ShowtimeSalesView_CalculatesCorrectMetricsViaLinq
Passed BetaGiaiPhong.Tests.IntegrationTests.QrScanningTests.TC_QR_03_ConcurrentScanOfSameTicket_ExactlyOneAccepted
Passed BetaGiaiPhong.Tests.IntegrationTests.QrScanningTests.TC_QR_01_ValidScan_AcceptsTicketAndMarksUsed
Passed BetaGiaiPhong.Tests.IntegrationTests.QrScanningTests.TC_QR_05_WrongShowtime_ReturnsWrongShowtime
Passed BetaGiaiPhong.Tests.IntegrationTests.QrScanningTests.TC_QR_04_ForgedToken_ReturnsNotFound
Passed BetaGiaiPhong.Tests.IntegrationTests.QrScanningTests.TC_QR_06_OutsideWindow_ReturnsOutsideWindow
Passed BetaGiaiPhong.Tests.IntegrationTests.QrScanningTests.TC_QR_02_ScanAlreadyUsedTicket_ReturnsAlreadyUsed
Passed BetaGiaiPhong.Tests.IntegrationTests.PaymentWorkflowTests.TC_PAY_03_DuplicateWebhookCallbacks_AreIdempotent
Passed BetaGiaiPhong.Tests.IntegrationTests.PaymentWorkflowTests.TC_PAY_02_PaymentFailure_ReleasesSeatAndMarksOrderFailed
Passed BetaGiaiPhong.Tests.IntegrationTests.PaymentWorkflowTests.TC_PAY_01_ValidPayment_IssuesTicketAndMarksOrderPaid
Passed BetaGiaiPhong.Tests.IntegrationTests.PaymentWorkflowTests.TC_PAY_05_InvalidHmacSignature_ThrowsUnauthorizedException
Passed BetaGiaiPhong.Tests.IntegrationTests.PaymentWorkflowTests.TC_PAY_06_AmountMismatch_RejectsPayment
Passed BetaGiaiPhong.Tests.IntegrationTests.PaymentWorkflowTests.TC_PAY_04_LatePayment_DoesNotReviveExpiredOrder_MarksNeedsReview
Passed BetaGiaiPhong.Tests.IntegrationTests.BookingConcurrencyTests.TC_CON_02_TwoConcurrentRequestsForDifferentSeats_BothSucceed
Passed BetaGiaiPhong.Tests.IntegrationTests.BookingConcurrencyTests.TC_CON_01a_TwoConcurrentRequestsForSameSeat_ExactlyOneSucceeds
Passed BetaGiaiPhong.Tests.IntegrationTests.BookingConcurrencyTests.TC_CON_04_ExpiredSeatReservation_ReclaimedByNewCustomer
Passed BetaGiaiPhong.Tests.IntegrationTests.BookingConcurrencyTests.TC_CON_01c_FiftyConcurrentRequestsForSameSeat_ExactlyOneSucceeds
Passed BetaGiaiPhong.Tests.IntegrationTests.BookingConcurrencyTests.TC_CON_03_OverlappingSeatRequests_MaintainsAllOrNothingAtomicity
Passed BetaGiaiPhong.Tests.IntegrationTests.BookingConcurrencyTests.TC_CON_01b_TenConcurrentRequestsForSameSeat_ExactlyOneSucceeds
Passed BetaGiaiPhong.Tests.IntegrationTests.BookingConcurrencyTests.TC_CON_05_InvalidSeatInRequest_RollsBackEntireTransaction
Passed BetaGiaiPhong.Tests.WebTests.AuthenticationAndAuthorizationWebTests.TC_WEB_AUTH_01_ValidCredentials_SignsInAndSetsCookie
Passed BetaGiaiPhong.Tests.WebTests.AuthenticationAndAuthorizationWebTests.TC_WEB_AUTH_02_InvalidPassword_ReturnsGenericErrorMessage_NoAuthCookie
Passed BetaGiaiPhong.Tests.WebTests.AuthenticationAndAuthorizationWebTests.TC_WEB_AUTH_03_NonExistentEmail_ReturnsGenericErrorMessage_NoLeakage
Passed BetaGiaiPhong.Tests.WebTests.AuthenticationAndAuthorizationWebTests.TC_WEB_AUTH_04_InactiveUser_IsRejectedWithAccountLockedMessage
Passed BetaGiaiPhong.Tests.WebTests.AuthenticationAndAuthorizationWebTests.TC_WEB_AUTH_05_UnauthenticatedUser_AccessingProfile_RedirectsToLogin
Passed BetaGiaiPhong.Tests.WebTests.AuthenticationAndAuthorizationWebTests.TC_WEB_AUTH_06_ApiMe_Unauthenticated_Returns401Unauthorized
Passed BetaGiaiPhong.Tests.WebTests.AuthenticationAndAuthorizationWebTests.TC_WEB_AUTH_07_AuthenticatedSession_CanAccessProfileAndApiMe
Passed BetaGiaiPhong.Tests.WebTests.PublicCatalogWebTests.TC_WEB_CAT_01_HomePage_Returns200AndRendersHeroAndCatalog
Passed BetaGiaiPhong.Tests.WebTests.PublicCatalogWebTests.TC_WEB_CAT_02_MoviesPage_OnlyDisplaysPublishedMovies
Passed BetaGiaiPhong.Tests.WebTests.PublicCatalogWebTests.TC_WEB_CAT_03_MovieDetails_ShowsMetadataAndGroupedShowtimes
Passed BetaGiaiPhong.Tests.WebTests.PublicCatalogWebTests.TC_WEB_CAT_04_SeatMap_RendersVisualLayoutAndPricingTiers
Passed BetaGiaiPhong.Tests.WebTests.PublicCatalogWebTests.TC_WEB_CAT_05_ApiMovies_ReturnsPublishedMoviesJson
Passed BetaGiaiPhong.Tests.WebTests.PublicCatalogWebTests.TC_WEB_CAT_06_ApiShowtimeSeats_CalculatesHeldAndAvailableStatesAccurately
Passed BetaGiaiPhong.Tests.WebTests.WebSecurityTests.TC_WEB_SEC_01_ResponseIncludesSecurityHeaders
Passed BetaGiaiPhong.Tests.WebTests.WebSecurityTests.TC_WEB_SEC_02_PostWithoutCsrfToken_IsRejected
Passed BetaGiaiPhong.Tests.WebTests.WebSecurityTests.TC_WEB_SEC_03_UnhandledException_ReturnsProblemDetailsJsonWithoutStackTrace
Passed BetaGiaiPhong.Tests.WebTests.WebSecurityTests.TC_WEB_SEC_04_CorrelationId_IsGeneratedAndPropagated
Passed BetaGiaiPhong.Tests.WebTests.WebSecurityTests.TC_WEB_SEC_05_ApiRoute_NonExistentResource_ReturnsProblemDetails404
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_01_Guest_PostReserve_Returns401Unauthorized
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_02_NonCustomerRole_PostReserve_Returns403Forbidden
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_03_Customer_ReserveSingleSeat_Returns201Created
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_04_Customer_ReserveMultipleSeats_Returns201Created
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_05_ReserveMoreThan8Seats_Returns400BadRequest
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_06_ReserveDuplicateSeats_Returns400BadRequest
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_07_ReserveEmptySeatList_Returns400BadRequest
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_08_ReserveAlreadyHeldSeat_Returns409Conflict
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_09_ReserveSeatFromAnotherRoom_ReturnsConflict
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_10_CustomerA_CannotViewCustomerB_Order_Returns404NotFound
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_11_CustomerA_CannotCancelCustomerB_Order_Returns403Or404
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_12_CancelPendingOrder_Succeeds_MarksCancelledAndReleasesSeats
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_13_CancelPaidOrder_Returns422UnprocessableEntity
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_14_CancelExpiredOrder_TransitionsToEXPIRED_Returns409Conflict
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_15_GetOrder_IsReadOnly_DoesNotMutateDatabase
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_16_PostReserve_WithoutAntiforgeryToken_Returns400BadRequest
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_17_OrderTotalVnd_MatchesOrderSeatsSnapshot
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_18_MvcSeatSelectionFlow_RedirectsToOrderDetails
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_19_GetMyOrders_PaginationAndList_ReturnsAccurateOrders
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_20_OrderLifecycleService_ExpireDueAsync_CleansUpExpiredOrders
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_21_MvcReserve_InvalidSeatCsvInput_RedirectsWithFlashError
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_22_ReservePastOrNonSaleShowtime_ReturnsConflict
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_23_ConcurrentCancelAndPayment_MaintainsDataConsistency
Passed BetaGiaiPhong.Tests.WebTests.BookingAndOrderWebTests.TC_WEB_BOOK_24_PostCancel_WithoutAntiforgeryToken_Returns400BadRequest
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_01_CreateMockAttempt_ValidPendingOrder_ReturnsAttemptDetails
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_02_CreateMockAttempt_Unauthenticated_Returns401
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_03_CreateMockAttempt_OtherCustomerOrder_Returns404
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_04_CreateMockAttempt_NonPendingOrder_Returns409Conflict
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_05_CreateMockAttempt_ExpiredReservation_MarksExpired_Returns409
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_06_Webhook_MissingSignatureHeader_Returns401Unauthorized
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_07_Webhook_InvalidSignature_Returns401Unauthorized
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_08_Webhook_TamperedAmount_Returns401Unauthorized
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_09_Webhook_ValidPayment_TransitionsToPAID_SeatsSold_TicketsIssued
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_10_Webhook_DuplicatePayload_Returns200Idempotent
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_11_Webhook_LatePaymentOnExpiredOrder_RecordsNeedsReview_NoTickets
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_12_Webhook_PaymentFailure_ReleasesSeatsAndMarksFailed
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_13_Webhook_ConcurrentCallbacks_MaintainsDataIntegrity
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_14_DemoPay_SimulatorDisabled_Returns404NotFound
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_15_DemoPay_CustomerOrder_ExecutesPaymentFlowAndRedirects
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_16_GetTickets_CustomerOrder_ReturnsTicketsWithVietnamTime
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_17_GetQrCode_ReturnsValidPng_DecryptsToValidToken
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_18_TicketApi_DoesNotLeakRawTokenOrCipher
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_19_GetQrCode_CrossCustomerAccess_Returns404NotFound
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_20_CheckoutPage_RendersCountdownTimerAndMockDisclaimer
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_21_CheckoutPage_ExpiredOrder_RedirectsToDetailsWithError
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_22_DatabaseInvariants_ZeroViolationsAfterPayments

Test Run Successful.
Total tests: 95 | Passed: 95 | Failed: 0 | Total time: ~51 Seconds
```

### 11.1. Running the Web Application
```powershell
# Launch the ASP.NET Core Web server (http://localhost:5145 / https://localhost:7097)
dotnet run --project .\src\BetaGiaiPhong.Web
```

---

## 12. Audit Baseline & Remediation Summary

| Issue ID | Severity | Description | Resolution Status |
| :--- | :--- | :--- | :--- |
| **ISSUE-01** | **P1** | `PendingModelChangesWarning` during migration execution | **RESOLVED**: Scaffolded `.Designer.cs` and compiled model snapshot. Zero pending changes. |
| **ISSUE-02** | **P1** | Security vulnerability warnings `NU1902`/`NU1903` | **RESOLVED**: Pinned `System.Security.Cryptography.Pkcs` to `10.0.14`. 0 vulnerabilities. |
| **ISSUE-03** | **P2** | Missing EF Core navigation properties on domain entities | **RESOLVED**: Added bidirectional navigation properties for all 22 FK relations. |
| **ISSUE-04** | **P2** | `01_prepare_dev_session.ps1` failure on Windows PowerShell 5.1 | **RESOLVED**: Replaced with cross-platform .NET crypto instance call. |
| **ISSUE-05** | **P2** | Unmapped reporting view `dbo.vw_ShowtimeSales` | **RESOLVED**: Mapped `ShowtimeSalesSummary` via `HasNoKey().ToView()`. |
| **BE-02-REV**| **P2** | Test isolation, order IDOR enumeration & MVC input validation | **RESOLVED**: Standardized 404 for cross-customer cancel; strict CSV parsing; legitimate mock payment in TC_WEB_BOOK_13; added 6th invariant check. |

---

## 13. Quality Verification Evidence

- **Compilation:** 0 errors, 0 warnings across all 4 projects (`Data`, `Runner`, `Web`, `Tests`) in Debug and Release.
- **EF Core Model Drift Check:** Clean (0 pending changes).
- **Vulnerability Audit:** Clean (0 vulnerable packages).
- **Test Suite Pass Rate:** 100% (95/95 tests passed: 28 Data/Domain tests + 67 Web integration tests).
- **Static Schema Check:** Pass (17 tables, 116 columns, 22 foreign keys verified via `scripts/static_schema_checks.py`).
- **Database Multi-Table Invariants:** 6/6 checks passed with 0 violations via `BetaGiaiPhong.Runner verify`.

---

## 14. Implementation Status & Roadmap

### 14.1. Phase BE-01 (Completed)
- **Host & Architecture:** Modular monolith host in `src/BetaGiaiPhong.Web` targeting .NET 10.
- **Identity & Authentication:** Custom PBKDF2 Cookie Authentication and Role Authorization (`CUSTOMER`, `STAFF`, `MANAGER`, `ADMIN`) mapped directly to existing relational tables `dbo.Users`, `dbo.Roles`, `dbo.UserRoles`.
- **Public Catalog:** Server-rendered Razor views and RESTful JSON APIs for browsing published movies, showtime schedules, and read-only interactive seat maps.
- **Security & Quality:** Anti-CSRF protection, HTTP security headers, login rate limiting, correlation ID tracking, RFC 7807 `ProblemDetails` error handling, and 18 automated Web integration tests.

### 14.2. Phase BE-02 (Completed — Seat Booking & Order Management)
- **Interactive Seat Selection:** Client-side selection on `/showtimes/{id}/seats` with dynamic chip rendering, pricing tally, 8-seat cap enforcement, and auto-submit.
- **Transactional Seat Reservation:** `POST /api/bookings/reserve` & `POST /bookings/reserve` leveraging `SeatReservationService` (`Serializable` isolation + row-level showtime locking), 5-minute hold timeout, and immutable price snapshots (`dbo.OrderSeats`).
- **Customer Order Management:** `GET /orders` & `GET /api/orders` (paged list), `GET /orders/{id}` & `GET /api/orders/{id}` (real-time countdown timer, seat snapshot), and strict ownership checks (`CUSTOMER` role + customer ID verification).
- **Customer Cancellation & Expired Order Handling:** `POST /api/orders/{id}/cancel` & `POST /orders/{id}/cancel` transitioning pending orders to `CANCELLED` and releasing held seats; auto-transitioning expired orders (`expires_utc <= now`) to `EXPIRED` state with seat release and returning 409 Conflict.
- **Security & Quality:** CSRF cookie token injection, authenticated claim protection, 20 dedicated integration tests (`TC_WEB_BOOK_01` to `TC_WEB_BOOK_20`).

### 14.3. Phase BE-03 (Completed — Mock Payment, Secure Webhook & Electronic Tickets)
- **Mock Payment Attempt API:** `POST /api/orders/{id}/payments/mock-attempt` generating client attempts with server-calculated price snapshots.
- **Customer Checkout UI:** `GET /orders/{id}/checkout` featuring real-time 5-minute reservation countdown, order summary, and clear academic mock payment disclaimer.
- **HMAC-SHA256 Webhook Listener:** `POST /api/webhooks/mock-payment` authenticated with `X-Signature-SHA256`, 32 KB payload limiter, constant-time validation, idempotent event processing, and automatic seat/order state transition (`PAID` vs `FAILED` vs `NEEDS_REVIEW`).
- **Development-Only Payment Simulator:** `POST /orders/{id}/demo-pay` enabled strictly in `Development` with `BGP_ENABLE_MOCK_PAYMENT_SIMULATOR=true`, signing payloads internally and executing full domain workflow.
- **Electronic Tickets & QR Code Streamer:** `GET /orders/{id}/tickets`, `GET /api/orders/{id}/tickets`, and `GET /tickets/{ticketId}/qr` dynamically rendering high-fidelity PNG QR codes via `QRCoder` (`PngByteQRCode`) with unsealed AES-256-GCM tokens, `Cache-Control: no-store`, and zero token/cipher leakage in JSON APIs.
- **Quality Assurance:** 22 dedicated integration and web tests (`TC_WEB_PAY_01` to `TC_WEB_PAY_22`), bringing the test suite to 95/95 PASS.

### 14.4. Upcoming Phases
- **Phase BE-04 (Staff Operations & Admin Dashboards):** Staff electronic ticket scanning terminal (`TicketScanningService`), movie/showtime publishing administrative portals, and hosted background worker for expired reservation sweeps.

---

## 15. Contributing & Commit Conventions

- Follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:
  - `feat:` New features
  - `fix:` Bug fixes
  - `refactor:` Code restructuring without behavior changes
  - `test:` Adding or updating tests
  - `chore:` Maintenance, dependency, and configuration updates
- Maintain `.editorconfig` formatting guidelines (4 spaces for C#, 2 spaces for JSON/Markdown).
- Ensure all tests pass (`dotnet test`) and no pending model changes exist before committing.

---

## 16. License & Authorship

- **Author:** Academic Capstone Team (`cakhiempn-a11y`).
- **Target Repository:** `cakhiempn-a11y/beta-giai-phong-ticketing-system` (Private).
- **License:** Educational and Academic Research Use Only.
