# Beta Giải Phóng — Online Cinema Ticketing System

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![EF Core 10](https://img.shields.io/badge/EF%20Core-10.0.14-512BD4?logo=dotnet)](https://learn.microsoft.com/ef/core/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-2025%20%7C%202022-CC292B?logo=microsoftsqlserver)](https://www.microsoft.com/sql-server/)
[![Build & Tests](https://img.shields.io/badge/Tests-166%20xUnit%20%2B%205%20E2E%20Passed%20%7C%200%20Failed-brightgreen)](tests/BetaGiaiPhong.Tests)
[![Security Scan](https://img.shields.io/badge/Vulnerabilities-0-brightgreen)](#7-security-architecture)

> **Academic Disclaimer:**  
> This software is an academic capstone prototype developed for the Management Information Systems (MIS) curriculum. It uses purely synthetic simulation data and is **not affiliated with, endorsed by, or integrated into the production systems or infrastructure of Beta Cinemas**. No real payment gateways or customer credentials are used.

---

## 1. Project Title & Academic Disclaimer

- **Project Title:** Analysis, Design, and Implementation of an Online Cinema Ticketing Management System for Beta Giải Phóng Cinema.
- **Context:** MIS Graduation Capstone Project.
- **Scope Boundary:** This repository encompasses the full backend ticketing management system: the foundational **Data Layer, Entity Framework Core Relational Modeling, Transactional Services, and Concurrency Verification Suite** (BE-01), **ASP.NET Core Web Application** providing Cookie Authentication, Role Authorization, Public Catalog, Interactive Seat Map, Transactional Reservation (BE-02), **Mock Payment, Secure HMAC Webhooks, Electronic Tickets & QR Code Generation** (BE-03), **Staff QR Ticket Check-in, Web Scanner & Periodic Reservation Expiry Background Worker** (BE-04A), and **Manager & Admin Operations, Showtime Publishing, Pricing & Sales Dashboard** (BE-04B). Real payment gateways and physical turnstile hardware are simulated using academic cryptographic contracts.

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

- **`BetaGiaiPhong.Web`:** ASP.NET Core host providing server-rendered Razor Views and RESTful JSON APIs for public catalog exploration (published movies, showtimes grouped by date, and real-time interactive seat maps), transactional seat reservation (1–8 seats with 5-minute hold), order lifecycle management, mock payment checkout with HMAC-SHA256 signature verification, electronic ticket issuance, dynamic QR code generation, staff check-in portal with browser camera & USB 2D barcode scanning (`/staff/check-in`), check-in history audit log viewer (`/staff/check-in/history`), background worker (`ReservationExpiryWorker`) for automatic cleanup of expired reservations, and management portal (`/management/movies`, `/management/showtimes`, `/management/rooms`, `/management/reports`) for movie lifecycle transitions, showtime publishing, and sales dashboard. Implements secure Cookie Authentication and role authorization (`CUSTOMER`, `STAFF`, `MANAGER`, `ADMIN`) mapped to `dbo.Users` without extra Identity tables.
- **`BetaGiaiPhong.Data`:** Contains 17 domain entities, `BetaDbContext`, Fluent API mappings (`BetaModelConfiguration`), EF Core Migrations, and core domain services (`SeatReservationService`, `PaymentService`, `TicketScanningService`, `OrderLifecycleService`, `ShowtimePublishingService`, `MovieManagementService`, `ManagementQueryService`).
- **`BetaGiaiPhong.Runner`:** Administrative CLI tool for applying database migrations, seeding synthetic demo data, and executing integration validation scenarios. Directly references `BetaGiaiPhong.Data`.
- **`BetaGiaiPhong.Tests`:** Comprehensive automated xUnit test suite (166 tests) covering unit tests, live SQL Server integration scenarios (high-thread concurrency, webhook idempotency, QR cryptographic verification, worker sweeps, database integrity invariants, and test database safety guards), and Web application integration tests via `WebApplicationFactory`.
- **`e2e/`:** Dedicated Playwright Real-Browser E2E test suite running Chromium against a live `BetaGiaiPhong.Web` server process and isolated ephemeral SQL Server database (`BetaGiaiPhong_E2E_<RunId>`), verifying the complete cross-role user journey, negative/security scenarios, server restart recovery, responsive/accessibility viewports, and 30-concurrent-request seat contention.

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
- **Secret Management:** Connection strings and cryptographic secrets are supplied exclusively via environment variables or .NET User Secrets (`dotnet user-secrets`) during local development (`ConnectionStrings__BetaDb`, `BGP_MOCK_HMAC_KEY_B64`, `BGP_QR_AES_KEY_B64`, `BGP_DEMO_PASSWORD`). No production or development credentials are committed to Git. The mock payment simulator (`BGP_ENABLE_MOCK_PAYMENT_SIMULATOR`) is disabled by default in the repository. Application startup strictly validates 256-bit CSPRNG key lengths (fail-closed).

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

> [!CAUTION]
> **Key Stability Warning:** `BGP_QR_AES_KEY_B64` encrypts and decrypts QR tokens in `dbo.Tickets`. **NEVER** rotate or overwrite this key on a database containing active tickets, or existing QR codes will fail decryption during staff check-in.

### 9.1. Environment Configuration
Cryptographic keys (`BGP_MOCK_HMAC_KEY_B64`, `BGP_QR_AES_KEY_B64`) and connection strings are managed via .NET User Secrets or environment variables to prevent accidental credential leakage into Git.

To configure stable 256-bit CSPRNG keys that persist across shell restarts:
```powershell
# Generate independent CSPRNG 32-byte keys and configure in .NET User Secrets
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$hmacBytes = New-Object byte[] 32; $rng.GetBytes($hmacBytes)
$aesBytes = New-Object byte[] 32; $rng.GetBytes($aesBytes)
$hmac = [Convert]::ToBase64String($hmacBytes)
$aes  = [Convert]::ToBase64String($aesBytes)
dotnet user-secrets set "BGP_MOCK_HMAC_KEY_B64" "$hmac" --project .\src\BetaGiaiPhong.Web
dotnet user-secrets set "BGP_QR_AES_KEY_B64" "$aes" --project .\src\BetaGiaiPhong.Web
Remove-Variable hmacBytes, aesBytes, hmac, aes -ErrorAction SilentlyContinue
```

> [!NOTE]
> `BetaGiaiPhong.Web` automatically loads `.NET User Secrets` in the `Development` environment. However, CLI utilities like `BetaGiaiPhong.Runner` run outside the Web host context and require environment variables. `scripts/01_prepare_dev_session.ps1` synchronizes keys from User Secrets directly into your PowerShell session.

Execute `scripts/01_prepare_dev_session.ps1` in Windows PowerShell 5.1 or PowerShell 7+:
```powershell
# Set your local SQL Server connection string
$env:ConnectionStrings__BetaDb = "Server=localhost;Database=BetaGiaiPhong_Dev;Trusted_Connection=True;TrustServerCertificate=True;"
$env:BGP_ALLOW_DEMO_SEED = "true"
$env:BGP_DEMO_PASSWORD = "DemoSecurePassword123!"

# Synchronizes persistent keys from User Secrets (or issues fallback ephemeral keys with warning)
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

# Seed an active showtime & generate a paid demo ticket for staff check-in
# Note: Requires BGP_ALLOW_CHECKIN_DEMO=true (set by 01_prepare_dev_session.ps1).
# Raw QR token is masked by default; set BGP_SHOW_DEMO_QR_TOKEN=true to display raw plaintext token.
dotnet run --project .\src\BetaGiaiPhong.Runner -- seed-checkin-demo
# Alternatively, run: .\scripts\03_seed_checkin_demo.ps1

# Execute end-to-end smoke verification
dotnet run --project .\src\BetaGiaiPhong.Runner -- smoke

# Run multi-table integrity invariants check
dotnet run --project .\src\BetaGiaiPhong.Runner -- verify
```

---

## 11. Running Automated Tests

The solution features a comprehensive automated xUnit test suite (`tests/BetaGiaiPhong.Tests`) consisting of **166 automated tests** spanning unit tests, live SQL Server integration tests, test database safety guards, and full ASP.NET Core web host tests via `CustomWebApplicationFactory`, complemented by a **5-suite Real-Browser Playwright E2E test suite** (`e2e/`).

### 11.1. Dynamic SQL Server Test Isolation & Safety Guards
Integration and Web tests dynamically create an ephemeral test database per test execution run (`BetaGiaiPhong_Test_<RunId>`) via `TestDatabaseFixture`, applying migrations automatically and disposing the database upon completion.

Configurable environment variables for test execution:
- `BGP_TEST_SQL_MASTER_CONNECTION`: Dedicated master database connection string (default: `Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;`). Does **not** fall back to `ConnectionStrings__BetaDb`.
- `BGP_TEST_ALLOW_DB_CREATE`: Explicit opt-in required (`true` / `1`) to allow creating ephemeral test databases.
- `BGP_TEST_DATABASE_PREFIX`: Prefix for ephemeral databases (default: `BetaGiaiPhong_Test_`).
- `BGP_TEST_AUTO_CLEANUP_DB`: When `true` (default), drops the ephemeral database on test completion after verifying ownership.
- `BGP_TEST_DATABASE_NAME`: Explicit pre-existing test database name. Requires `BGP_TEST_ALLOW_EXISTING_DB_REUSE=true` if the database already exists, and **never** drops pre-existing databases on dispose.
- `BGP_TEST_CONFIRM_REMOTE_SQL_SERVER`: Required (`true`) if the SQL Server `Data Source` is non-local (`localhost`, `127.0.0.1`, `(local)`, `.`).

> [!IMPORTANT]
> **Strict Ownership & Safety Guards**: `TestDatabaseFixture` strictly rejects connection strings or database names containing `prod`/`production`, prohibits running against `BetaGiaiPhong_Dev`, `master`, `model`, `msdb`, or `tempdb`, stamps newly created databases with a SQL Extended Property (`BGP_TestRunOwnershipToken`), and verifies that exact token before executing `DROP DATABASE`. Pre-existing databases are **never** dropped by `DisposeAsync()`.

### 11.2. Executing Tests

**Windows Developer Machine (xUnit Suite):**
```powershell
# Run all 166 unit, integration, safety, and web tests
$env:BGP_TEST_ALLOW_DB_CREATE = "true"
dotnet test .\BetaGiaiPhong.slnx --logger "console;verbosity=normal"
```

**Real-Browser Playwright E2E Suite (`e2e/`):**
```powershell
# Install Playwright dependencies and Chromium browser (first time)
cd .\e2e
npm ci
npx playwright install chromium

# Execute all 5 real-browser E2E test suites against a real Web server & isolated E2E database
npx playwright test
cd ..
```

**Docker Containerized SQL Server:**
```powershell
# Spin up local SQL Server container
docker compose up -d

# Point test runner to containerized master
$env:BGP_TEST_SQL_MASTER_CONNECTION = "Server=localhost,1433;Database=master;User Id=sa;Password=StrongLocalDevPassword123!;TrustServerCertificate=True;"
$env:BGP_TEST_ALLOW_DB_CREATE = "true"
dotnet test .\BetaGiaiPhong.slnx
```

**Continuous Integration (GitHub Actions):**
Automated CI is configured in `.github/workflows/ci.yml` running on `ubuntu-latest` with a containerized SQL Server 2022 service container, executing 4 jobs:
1. `build-and-validate`: SDK check, Release build, static schema check, EF model drift check, and vulnerability scan.
2. `automated-tests`: Full 166-test xUnit execution with TRX and Cobertura code coverage reporting.
3. `integrity-checks`: Migration, synthetic demo seeding, smoke verification, concurrency race test, and 9/9 runner invariants check.
4. `e2e-browser-tests`: Headless Chromium Playwright E2E suite against a real `BetaGiaiPhong.Web` process and isolated SQL Server 2022 E2E database, uploading Playwright HTML reports, traces, screenshots, and runner logs on failure.

### 11.3. Test Suite Execution Output
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
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_04b_GetPaymentAttempt_Owner_Returns200_CrossCustomer_Returns404
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
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_17b_GetQrCode_ScannedOutsideWindow_ReturnsOutsideWindow
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_18_TicketApi_DoesNotLeakRawTokenOrCipher
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_19_GetQrCode_CrossCustomerAccess_Returns404NotFound
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_20_CheckoutPage_RendersCountdownTimerAndMockDisclaimer
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_21_CheckoutPage_ExpiredOrder_RedirectsToDetailsWithError
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_22_TicketApi_DoesNotLeakRawTokenOrCipher
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_23_DemoPay_SimulatorDisabled_Returns404NotFound
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_24_DemoPay_SimulatorEnabled_CustomerOrder_ExecutesPaymentFlow
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_25_DemoPay_WithoutCsrfToken_Returns400BadRequest
Passed BetaGiaiPhong.Tests.WebTests.MockPaymentAndTicketWebTests.TC_WEB_PAY_26_DatabaseInvariants_ZeroViolationsAfterPayments

Passed BetaGiaiPhong.Tests.WebTests.ManagementWebTests.TC_MGMT_27_AuditTrail_RecordsCriticalActions
Passed BetaGiaiPhong.Tests.WebTests.ManagementWebTests.TC_MGMT_28_Dashboard_IndependentFinancialReconciliation_CalculatesCorrectly
Passed BetaGiaiPhong.Tests.WebTests.ManagementWebTests.TC_MGMT_29_MovieArchive_ShowtimePublish_Concurrency_PreservesInvariants
Passed BetaGiaiPhong.Tests.IntegrationTests.TestDatabaseSafetyTests.TC_SAFE_01_PreExistingDatabase_WithoutReuseOptIn_ThrowsAndNeverDrops
Passed BetaGiaiPhong.Tests.IntegrationTests.TestDatabaseSafetyTests.TC_SAFE_02_PreExistingDatabase_WithReuseOptIn_ReusesAndNeverDropsOnDispose
Passed BetaGiaiPhong.Tests.IntegrationTests.TestDatabaseSafetyTests.TC_SAFE_03_NewlyCreatedDatabase_HasOwnershipToken_AndIsDroppedOnDispose
Passed BetaGiaiPhong.Tests.IntegrationTests.TestDatabaseSafetyTests.TC_SAFE_04_ProtectedDatabaseNames_AreStrictlyRejected
Passed BetaGiaiPhong.Tests.IntegrationTests.TestDatabaseSafetyTests.TC_SAFE_05_AllowDbCreateFalse_RefusesToCreateNewDatabase

Test Run Successful.
Total tests: 166 | Passed: 166 | Failed: 0 | Total time: ~1m 20s
```

### 11.4. Running the Web Application
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
| **BE-05B-SAFE**| **P1** | `TestDatabaseFixture` pre-existing database drop hazard | **RESOLVED**: Enforced explicit `BGP_TEST_ALLOW_DB_CREATE=true` opt-in, `BGP_TEST_ALLOW_EXISTING_DB_REUSE=true`, and SQL Extended Property ownership token (`BGP_TestRunOwnershipToken`) verification before `DROP DATABASE`. |

---

## 13. Quality Verification Evidence

- **Compilation:** 0 errors, 0 warnings across all 4 projects (`Data`, `Runner`, `Web`, `Tests`) in Debug and Release.
- **EF Core Model Drift Check:** Clean (0 pending changes).
- **Vulnerability Audit:** Clean (0 vulnerable packages).
- **xUnit Test Suite Pass Rate:** 100% (166/166 tests passed: 33 Data/Domain/Safety tests + 133 Web integration tests).
- **Playwright Real-Browser E2E Pass Rate:** 100% (5/5 E2E suites passed in Headless Chromium against live `BetaGiaiPhong.Web` and SQL Server).
- **Static Schema Check:** Pass (17 tables, 116 columns, 22 foreign keys verified via `scripts/static_schema_checks.py`).
- **Database Multi-Table Invariants:** 9/9 checks passed with 0 violations via `BetaGiaiPhong.Runner verify`.

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
- **Quality Assurance:** 22 dedicated integration and web tests (`TC_WEB_PAY_01` to `TC_WEB_PAY_22`), bringing the test suite to 104/104 PASS.

### 14.4. Phase BE-04A (Completed — Staff Check-in, QR Scanner & Reservation Expiry Worker)
- **Staff Electronic Ticket Scanning:** `POST /api/staff/tickets/scan` with HMAC token unsealing, status transitions (`ISSUED` -> `USED`), concurrency guards, and check-in history recording (`dbo.TicketCheckIns`).
- **Staff Check-In UI:** `GET /staff/check-in` featuring real-time camera/USB barcode scanning with debouncing, error handling, and manual code lookup; `GET /staff/check-in/history` for audit log review.
- **Automated Reservation Expiry Worker:** `ReservationExpiryWorker` running periodic sweeps with application locks and graceful cancellation handling.
- **Quality Assurance:** 28 integration and web tests (`TC_QR_01` to `TC_QR_06`, `TC_WORKER_01` to `TC_WORKER_07`, and `TC_WEB_STAFF_01` to `TC_WEB_STAFF_14`).

### 14.5. Phase BE-04B (Completed — Manager & Admin Operations, Showtime Publishing & Sales Dashboard)
- **Manager Security & Dynamic Principal Validation:** `RequireManager` authorization policy with dynamic database account check (`OnValidatePrincipal`) rejecting locked/disabled accounts and dynamically revoking roles across all roles (`CUSTOMER`, `STAFF`, `MANAGER`, `ADMIN`).
- **Movie Lifecycle Management:** Complete state machine (`DRAFT` -> `PUBLISHED` -> `ARCHIVED`), strict duration lock when active showtimes exist, automatic showtime closure on archive, bookable showtime guards in `SeatReservationService`, age label validation, safe poster URL sanitization, and audit logging.
- **Showtime Management & Publishing:** Integrated with `ShowtimePublishingService.PublishAsync`, Vietnam local time to UTC conversion, room schedule overlap check, multi-criteria filtering, and ticketed showtime cancellation safeguards with automatic in-flight pending order cancellation and held seat release.
- **Room & Physical Seat Reference:** Read-only inspection of room layouts, capacities, and physical seat matrices.
- **Sales & Operations Dashboard:** Grounded in `dbo.vw_ShowtimeSales`, calculating valid paid revenue, tickets sold, and mathematically correct weighted occupancy.
- **Quality Assurance:** 27 dedicated web integration tests (`TC_MGMT_01` to `TC_MGMT_27`), bringing the test suite to 159/159 PASS.

### 14.6. Phase BE-05A (Completed — SQL Test Isolation, GitHub Actions CI, Security & Regression Assurance)
- **Dynamic Ephemeral SQL Server Test Isolation:** Refactored `TestDatabaseFixture` to generate isolated, ephemeral test databases (`BetaGiaiPhong_Test_<RunId>`) per test run with strict production and development guard rails, preventing state pollution and ensuring repeatability across Windows local environments and Linux CI containers.
- **GitHub Actions CI Automation:** Implemented `.github/workflows/ci.yml` featuring 3 parallel and sequential jobs:
  1. `build-and-validate`: SDK setup, tool restore (`dotnet-ef`), Release build (0 warnings, 0 errors), static schema checks, EF Core model drift verification, and NuGet vulnerability audits.
  2. `automated-tests`: Containerized SQL Server 2022 service container, running the full 161-test suite with TRX and Cobertura code coverage reporting.
  3. `integrity-checks`: Standalone SQL Server container applying migrations, seeding demo catalog, running smoke test, high-concurrency race test, and verifying 9/9 database invariants.
- **Concurrency Locking & Invariant Preservation:** Enforced strict hierarchical lock ordering (`SqlServerLocks.MovieAsync` followed by `RoomAsync` under `IsolationLevel.Serializable`) across `ShowtimePublishingService` and `MovieManagementService`, proving that archived movies cannot possess active future showtimes (`TC_MGMT_29`).
- **Independent Financial Assertions:** Built independent mathematical reconciliation tests for the Sales & Operations Dashboard (`TC_MGMT_28`), validating revenue, ticket counts, and weighted occupancy calculations across timezone boundaries without circular dependencies on `dbo.vw_ShowtimeSales`.
- **Quality Assurance:** 161/161 tests PASS (100% pass rate) with 86.78% overall code coverage (97.85% in `BetaGiaiPhong.Data`).

### 14.7. Phase BE-05B (Completed — Real Browser E2E Testing, Security, Performance, Recovery & User Acceptance)
- **Critical Test Database Safety Hardening:** Eliminated pre-existing database drop hazards in `TestDatabaseFixture` via explicit `BGP_TEST_ALLOW_DB_CREATE=true` opt-in, `BGP_TEST_ALLOW_EXISTING_DB_REUSE=true`, remote host confirmation guards, and SQL Server Extended Property ownership tokens (`BGP_TestRunOwnershipToken`), verified by 5 dedicated safety regression tests (`TC_SAFE_01` to `TC_SAFE_05`).
- **Real-Browser Playwright E2E Suite (`e2e/`):** Full Chromium browser automation against a live `BetaGiaiPhong.Web` Kestrel process and isolated `BetaGiaiPhong_E2E_<RunId>` SQL Server database:
  1. `01-full-journey.spec.ts`: End-to-end cross-role journey (**Manager** movie/showtime publishing $\rightarrow$ **Customer** seat selection, checkout, simulated payment, QR PNG download $\rightarrow$ **Staff** in-browser `Html5Qrcode` PNG decoding, `ACCEPTED` / `ALREADY_USED` check-in, history audit $\rightarrow$ **Manager** sales dashboard verification).
  2. `02-negative-and-security.spec.ts`: Browser concurrency conflict (`409`), >8 seat limit enforcement, expired order checkout block, cross-customer IDOR (`404`), `WRONG_SHOWTIME` / `OUTSIDE_WINDOW` / `NOT_FOUND` QR scan rejections, ticketed showtime cancellation block, archived movie booking block, dynamic role revocation & account lock on next request, CSRF validation, and zero secret leakage in console/network.
  3. `03-restart-recovery.spec.ts`: Live `BetaGiaiPhong.Web` process termination and restart with preserved paid orders/tickets, duplicate signed webhook idempotency, network failure banner (`LỖI KẾT NỐI`) and retry recovery on `/staff/check-in`, `ReservationExpiryWorker` background reclamation, and 9/9 runner invariants verification.
  4. `04-responsive-and-a11y.spec.ts`: Multi-viewport verification (`1440x900` Desktop, `768x1024` Tablet, `390x844` Mobile), form accessibility labels, keyboard focus navigation, QR minimum rendering dimensions ($\ge 120\text{px}$), and Manual Physical Camera UAT checklist.
  5. `05-performance-load.spec.ts`: Page & API latency benchmarking (P95 well below thresholds) and 30 concurrent seat reservation requests on a single seat (1 winner `201`, 29 conflicts `409`, 0 server errors, 0 deadlocks).
- **CI Pipeline Integration:** Added `e2e-browser-tests` job to `.github/workflows/ci.yml` with Playwright report/trace artifact uploads on failure.

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
