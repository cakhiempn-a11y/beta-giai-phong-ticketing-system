# Beta Giải Phóng — Online Cinema Ticketing System Data Layer

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![EF Core 10](https://img.shields.io/badge/EF%20Core-10.0.14-512BD4?logo=dotnet)](https://learn.microsoft.com/ef/core/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-2025%20%7C%202022-CC292B?logo=microsoftsqlserver)](https://www.microsoft.com/sql-server/)
[![Build & Tests](https://img.shields.io/badge/Tests-28%20Passed%20%7C%200%20Failed-brightgreen)](tests/BetaGiaiPhong.Tests)
[![Security Scan](https://img.shields.io/badge/Vulnerabilities-0-brightgreen)](#7-security-architecture)

> **Academic Disclaimer:**  
> This software is an academic capstone prototype developed for the Management Information Systems (MIS) curriculum. It uses purely synthetic simulation data and is **not affiliated with, endorsed by, or integrated into the production systems or infrastructure of Beta Cinemas**. No real payment gateways or customer credentials are used.

---

## 1. Project Title & Academic Disclaimer

- **Project Title:** Analysis, Design, and Implementation of an Online Cinema Ticketing Management System for Beta Giải Phóng Cinema.
- **Context:** MIS Graduation Capstone Project.
- **Scope Boundary:** This repository encompasses the foundational **Data Layer, Entity Framework Core Relational Modeling, Transactional Services, and Concurrency Verification Suite**. Web/API controllers, customer frontend interfaces, and production payment integrations are slated for the subsequent backend implementation phase.

---

## 2. Architectural Overview

The solution adheres to clean data-driven design patterns:
```
                      ┌───────────────────────────────────────┐
                      │        BetaGiaiPhong.Runner           │
                      │  (CLI Migration, Seed & Verification) │
                      └──────────────────┬────────────────────┘
                                         │ references
                                         ▼
                      ┌───────────────────────────────────────┐
                      │         BetaGiaiPhong.Data            │
                      │  (Entities, DbContext, Migrations,    │
                      │   Domain Services, Cryptography)      │
                      └──────────────────▲────────────────────┘
                                         │ references
                      ┌──────────────────┴────────────────────┐
                      │         BetaGiaiPhong.Tests           │
                      │  (xUnit: Unit & Concurrency Tests)    │
                      └───────────────────────────────────────┘
```

- **`BetaGiaiPhong.Data`:** Contains 17 domain entities, `BetaDbContext`, Fluent API mappings (`BetaModelConfiguration`), EF Core Migrations, and core domain services (`SeatReservationService`, `PaymentService`, `TicketScanningService`, `OrderLifecycleService`, `ShowtimePublishingService`).
- **`BetaGiaiPhong.Runner`:** Administrative CLI tool for applying database migrations, seeding synthetic demo data, and executing integration validation scenarios.
- **`BetaGiaiPhong.Tests`:** Comprehensive automated xUnit test suite covering unit tests and live SQL Server integration scenarios (high-thread concurrency, webhook idempotency, QR cryptographic verification, and database integrity invariants).

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
│   └── BetaGiaiPhong.Runner/              # Administrative CLI console application
└── tests/
    └── BetaGiaiPhong.Tests/               # xUnit unit & integration test suite
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
- **CR-02 (Seat Reservation Limit):** A single order may contain at most 8 seats.
- **CR-03 (Lock Duration):** Default seat hold duration is 5 minutes from reservation time.
- **CR-04 (Pessimistic Row Locking):** Seat reservation queries use `WITH (UPDLOCK, ROWLOCK)` within an explicit transaction to prevent duplicate holds.
- **CR-05 (All-or-Nothing Atomicity):** If any requested seat is unavailable, the entire reservation transaction rolls back.
- **CR-06 (Price Immutability):** Unit prices are captured in `dbo.OrderSeats` at reservation time and never updated even if showtime price tiers change.
- **CR-07 (Webhook Idempotency):** Duplicate payment webhook deliveries are acknowledged without re-processing or duplicate ticket generation.
- **CR-08 (QR Single-Use):** Ticket tokens can be redeemed exactly once. Concurrent scans result in at most one `ACCEPTED` outcome.
- **CR-09 (Scan Time Window):** Tickets may only be scanned within a designated time window relative to showtime start (from 60 minutes before start up to 30 minutes after start).

---

## 7. Security Architecture

- **Dependency Hygiene:** `System.Security.Cryptography.Pkcs` is explicitly pinned to version `10.0.14`, completely resolving `NU1902`/`NU1903` vulnerabilities (GHSA-846f-f764-76mx).
- **Webhook Authentication:** Webhooks are authenticated via `HMAC-SHA256` signatures verified using constant-time comparison against an application secret.
- **QR Token Cryptography:**
  - Plaintext tokens are never stored in the database.
  - Indexing: SHA-256 digest stored in `dbo.Tickets.qr_hash`.
  - Storage: Authenticated AES-256-GCM ciphertext in `dbo.Tickets.qr_token_cipher`.
- **Secret Management:** Connection strings and cryptographic secrets are supplied exclusively via environment variables (`ConnectionStrings__BetaDb`, `BGP_HMAC_SECRET`, `BGP_DEMO_PASSWORD`). No production or development credentials are committed.

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

Test Run Successful.
Total tests: 28 | Passed: 28 | Failed: 0 | Total time: ~36 Seconds
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

---

## 13. Quality Verification Evidence

- **Compilation:** 0 errors, 0 warnings across all 3 projects.
- **EF Core Model Drift Check:** Clean (0 pending changes).
- **Vulnerability Audit:** Clean (0 vulnerable packages).
- **Test Suite Pass Rate:** 100% (28/28 tests passed).
- **Static Schema Check:** Pass (17 tables, 116 columns, 22 foreign keys verified via `scripts/static_schema_checks.py`).

---

## 14. Future Roadmap: ASP.NET Core Backend

With the data and concurrency foundation verified, the proposed next implementation phase includes:
1. **Clean Architecture Separation:** Introduce `BetaGiaiPhong.Core` (DTOs, domain interfaces) and `BetaGiaiPhong.Web` (ASP.NET Core Web API / Minimal Endpoints).
2. **ASP.NET Core Identity Integration:** Wire authentication cookies/tokens against existing `dbo.Users`, `dbo.Roles`, and `dbo.UserRoles`.
3. **RESTful Web API Endpoints:** Expose public and staff endpoints for movie listings, seat layout visualization, booking reservations, mock payment checkout, and high-speed QR ticket scanning.
4. **Interactive Customer Web UI:** Responsive booking frontend built with Razor Pages or modern SPA client.

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
