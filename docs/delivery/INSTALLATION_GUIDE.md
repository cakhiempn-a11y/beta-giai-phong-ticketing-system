# HƯỚNG DẪN CÀI ĐẶT, CẤU HÌNH VÀ VẬN HÀNH HỆ THỐNG (`INSTALLATION_GUIDE`)

**Đề tài:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Tác giả:** Vũ Văn Khiêm (`11235581`) — Ngành Hệ thống thông tin quản lý (MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương  
**Phiên bản:** 1.1 (RC-01 Reconciled)

---

## 1. Yêu cầu Hệ thống và Công cụ Tiên quyết

| Thành phần | Phiên bản yêu cầu | Mục đích sử dụng | Lệnh kiểm tra nhanh |
|---|---|---|---|
| **Hệ điều hành** | Windows 10/11 (x64) hoặc Ubuntu 22.04/24.04 | Môi trường máy trạm phát triển hoặc máy chủ CI | `winver` / `uname -a` |
| **.NET SDK** | **.NET 10.0 SDK** (`10.0.100` trở lên, theo `global.json`) | Biên dịch và chạy `Data`, `Runner`, `Web`, `Tests` | `dotnet --version` |
| **EF Core CLI** | `dotnet-ef` `10.0.0` (khai báo trong `.config/dotnet-tools.json`) | Kiểm tra và áp dụng EF Core Migrations | `dotnet tool restore` |
| **SQL Server** | **SQL Server 2022** (Developer / Express / Docker) | Hệ quản trị CSDL quan hệ lưu trữ 17 bảng và View | `sqlcmd -?` |
| **Node.js & npm** | Node.js `20.x` hoặc `22.x` LTS | Thực thi bộ kiểm thử trình duyệt thực Playwright E2E (`e2e/`) | `node -v` & `npm -v` |
| **Python** | Python `3.10+` | Chạy công cụ kiểm tra tĩnh schema `scripts/static_schema_checks.py` | `python --version` |

---

## 2. Tải Mã nguồn và Khôi phục Gói Phụ thuộc

Mở PowerShell tại thư mục làm việc (`D:\Projects\BetaGiaiPhong`):

```powershell
# 1. Khôi phục công cụ local (dotnet-ef) và các gói NuGet của toàn bộ Solution
dotnet tool restore
dotnet restore BetaGiaiPhong.slnx

# 2. Biên dịch kiểm tra ở cấu hình Release
dotnet build BetaGiaiPhong.slnx -c Release --no-restore

# 3. (Tùy chọn cho kiểm thử E2E) Cài đặt gói Playwright và trình duyệt Chromium
Push-Location e2e
npm ci
npx playwright install chromium
Pop-Location
```

---

## 3. Cấu hình Biến Môi trường và Khóa Mật mã CSPRNG (`.NET User Secrets`)

Theo yêu cầu bảo mật `NFR-10`, kho mã nguồn **không lưu cứng (hard-code)** mật khẩu hay khóa mật mã trong `appsettings.json`. Các biến môi trường và khóa cấu hình gồm:

| Tên biến môi trường / User Secret | Ý nghĩa | Giá trị / Quy tắc bắt buộc |
|---|---|---|
| `ConnectionStrings__BetaDb` | Chuỗi kết nối SQL Server cho ứng dụng Web và Runner | Mặc định trên Windows: `Server=localhost;Database=BetaGiaiPhong_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False` |
| `BGP_QR_AES_KEY_B64` | Khóa đối xứng **AES-256** (đúng **32 byte** mã hóa Base64) dùng để niêm phong/giải mã `Tickets.qr_token_cipher` | Sinh một lần bằng **CSPRNG (`RandomNumberGenerator`)** và lưu vào `.NET User Secrets` của `src/BetaGiaiPhong.Web` |
| `BGP_MOCK_HMAC_KEY_B64` | Khóa bí mật **HMAC-SHA256** (tối thiểu **32 byte** Base64) để ký và xác thực Webhook thanh toán | Sinh một lần bằng **CSPRNG (`RandomNumberGenerator`)** và lưu vào `.NET User Secrets` của `src/BetaGiaiPhong.Web` |
| `BGP_DEMO_PASSWORD` | Mật khẩu khởi tạo cho 5 tài khoản demo khi chạy `seed-demo` hoặc `seed-checkin-demo` | Bắt buộc tối thiểu **12 ký tự** (ví dụ: `BetaDemo@2026!`) |
| `BGP_ALLOW_DEMO_SEED` | Cho phép chạy lệnh `seed-demo` (`true`/`false`) | `true` (được `scripts/01_prepare_dev_session.ps1` bật sẵn) |
| `BGP_ALLOW_CHECKIN_DEMO` | Cho phép chạy lệnh `seed-checkin-demo` (`true`/`false`) | `true` (được `scripts/01_prepare_dev_session.ps1` bật sẵn) |
| `BGP_TMDB_READ_TOKEN` | *(Tùy chọn)* Khóa đọc TMDB API v3 phía server phục vụ tra cứu mở rộng tại `/management/movies` | Để trống nếu chạy offline — hệ thống tự động vận hành ở **Chế độ không khóa TMDB (`Wikidata`/`IMDb` sẵn sàng)** |
| `MockPayment__AllowDemoTrigger` | Bật nút mô phỏng thanh toán trực tiếp trên trang Checkout (`POST /orders/{id}/demo-pay`) | Mặc định `true` trong `appsettings.Development.json` |

### 3.1. Khởi tạo Khóa Mật mã Ổn định bằng CSPRNG (Chỉ chạy 1 lần duy nhất khi cài máy mới)

Tuyệt đối **không dùng `Get-Random`** để sinh khóa mật mã. Hãy chạy đoạn lệnh PowerShell sử dụng `System.Security.Cryptography.RandomNumberGenerator` dưới đây **một lần duy nhất** để lưu khóa ổn định vào `.NET User Secrets` của dự án `src/BetaGiaiPhong.Web`:

```powershell
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$hmacBytes = New-Object byte[] 32; $rng.GetBytes($hmacBytes)
$aesBytes  = New-Object byte[] 32; $rng.GetBytes($aesBytes)
$hmac = [Convert]::ToBase64String($hmacBytes)
$aes  = [Convert]::ToBase64String($aesBytes)

dotnet user-secrets set "BGP_MOCK_HMAC_KEY_B64" "$hmac" --project .\src\BetaGiaiPhong.Web
dotnet user-secrets set "BGP_QR_AES_KEY_B64" "$aes" --project .\src\BetaGiaiPhong.Web
Remove-Variable hmacBytes, aesBytes, hmac, aes -ErrorAction SilentlyContinue
```

> [!CAUTION]
> **CẢNH BÁO QUAN TRỌNG VỀ TÍNH ĐỒNG BỘ CỦA KHÓA `BGP_QR_AES_KEY_B64`:**  
> - Cột `Tickets.qr_token_cipher` trong SQL Server được mã hóa bằng thuật toán `AES-256-GCM` với khóa `BGP_QR_AES_KEY_B64` tại thời điểm phát hành vé.  
> - Trong môi trường `Development`, dự án `BetaGiaiPhong.Web` tự động đọc `BGP_QR_AES_KEY_B64` và `BGP_MOCK_HMAC_KEY_B64` từ `.NET User Secrets`, nhưng công cụ dòng lệnh `BetaGiaiPhong.Runner` đọc từ biến môi trường của phiên PowerShell.  
> - Do đó, trước khi chạy `Runner` hoặc `Web` trong một cửa sổ PowerShell mới, hãy luôn gọi `. .\scripts\01_prepare_dev_session.ps1` để script tự động đọc khóa từ `.NET User Secrets` của `src/BetaGiaiPhong.Web` và đồng bộ ra biến môi trường hiện hành. Tuyệt đối không xoay (rotate) `BGP_QR_AES_KEY_B64` nếu cơ sở dữ liệu đã có vé đã phát hành!

---

## 4. Khởi tạo Cơ sở Dữ liệu (EF Core Migrations) và Nạp Dữ liệu Mẫu (Demo Seeding)

Thư mục `scripts/` cung cấp đúng 3 tệp công cụ thực tế:
- `scripts/01_prepare_dev_session.ps1`: Thiết lập `ConnectionStrings__BetaDb`, bật `BGP_ALLOW_DEMO_SEED=true`, `BGP_ALLOW_CHECKIN_DEMO=true` và đồng bộ `BGP_QR_AES_KEY_B64` + `BGP_MOCK_HMAC_KEY_B64` từ `.NET User Secrets` ra biến môi trường của phiên PowerShell hiện tại.
- `scripts/02_run_dev.ps1`: Chạy toàn bộ chuỗi kiểm định cục bộ trên `BetaGiaiPhong.Runner` (`migrate`, `seed-demo`, `status`, `smoke`, `race`, `late`, `verify`).
- `scripts/static_schema_checks.py`: Kiểm tra tĩnh cấu trúc 17 bảng, 145 cột (bao gồm dữ liệu xuất xứ phim thật xác minh), 39 SQL batches và 22 khóa ngoại `NO ACTION`.

### Cách 1: Chuẩn bị cơ sở dữ liệu sạch cho phiên Demo / Phát triển

Trong cùng một cửa sổ PowerShell tại `D:\Projects\BetaGiaiPhong`:

```powershell
# Bước 1: Đặt mật khẩu cho 5 tài khoản demo (bắt buộc >= 12 ký tự) và nạp cấu hình phiên
$env:BGP_DEMO_PASSWORD = "BetaDemo@2026!"
. .\scripts\01_prepare_dev_session.ps1

# Bước 2: Áp dụng 2 EF Core Migrations (tạo 17 bảng, 22 FK và View dbo.vw_ShowtimeSales)
dotnet run --project .\src\BetaGiaiPhong.Runner -- migrate

# Bước 3: Nạp dữ liệu danh mục mẫu (5 tài khoản, 2 phòng chiếu, sơ đồ ghế, phim và suất chiếu mẫu)
dotnet run --project .\src\BetaGiaiPhong.Runner -- seed-verified-showcase

# Bước 4 (Chỉ chạy khi muốn có sẵn 1 suất chiếu ở now + 15 phút và 1 vé PAID để demo quét vé ngay):
# Lưu ý: Nếu bạn định tự dùng tài khoản Manager tạo suất chiếu mới ở Phòng 1 tại now + 15 phút, KHÔNG chạy lệnh này để tránh trùng lịch Phòng 1.
dotnet run --project .\src\BetaGiaiPhong.Runner -- seed-checkin-demo

# Bước 5: Kiểm tra 9 nhóm bất biến toàn vẹn SQL Server
dotnet run --project .\src\BetaGiaiPhong.Runner -- verify
```

### Cách 2: Chạy kịch bản kiểm định tự động toàn trình (`02_run_dev.ps1`)

Nếu muốn chạy kiểm định nhanh toàn bộ các bài toán nghiệp vụ (`migrate`, `seed-demo`, `status`, `smoke`, `race`, `late`, `verify`) trên dòng lệnh:

```powershell
$env:BGP_DEMO_PASSWORD = "BetaDemo@2026!"
. .\scripts\01_prepare_dev_session.ps1
.\scripts\02_run_dev.ps1
```

### Danh sách 5 Tài khoản Demo sau khi chạy `seed-demo`

Mật khẩu của toàn bộ 5 tài khoản dưới đây là giá trị biến `$env:BGP_DEMO_PASSWORD` (ví dụ `BetaDemo@2026!`) mà bạn đã đặt trước khi chạy `seed-demo`:

| Vai trò (Role) | Email đăng nhập | Quyền hạn chính |
|---|---|---|
| **`ADMIN`** | `admin@demo.invalid` | Toàn quyền hệ thống và quản lý vận hành rạp |
| **`MANAGER`** | `manager@demo.invalid` | Quản lý phim, xem phòng/sơ đồ ghế, công bố suất chiếu & giá vé, xem Báo cáo doanh thu (`/management/reports`) |
| **`STAFF`** | `staff@demo.invalid` | Quét mã QR soát vé tại quầy (`/staff/check-in`) và xem lịch sử soát vé (`/staff/check-in/history`) |
| **`CUSTOMER` #1** | `customer1@demo.invalid` | Đặt ghế, giữ chỗ, thanh toán mô phỏng, hủy đơn chờ và nhận vé điện tử QR |
| **`CUSTOMER` #2** | `customer2@demo.invalid` | Tài khoản khách hàng thứ hai dùng để thử nghiệm tranh chấp ghế đồng thời và chống IDOR |

---

## 5. Khởi chạy Ứng dụng Web (`BetaGiaiPhong.Web`)

Trong cùng cửa sổ PowerShell đã chạy `. .\scripts\01_prepare_dev_session.ps1`, khởi động máy chủ Web bằng lệnh:

```powershell
# Chạy profile HTTP (cổng 5145 theo Properties/launchSettings.json)
dotnet run --project .\src\BetaGiaiPhong.Web --launch-profile http

# Hoặc chạy profile HTTPS (cổng 7097 và 5145 — dùng khi cần bật Camera trình duyệt qua HTTPS)
dotnet run --project .\src\BetaGiaiPhong.Web --launch-profile https
```

- **Địa chỉ truy cập giao diện Web (HTTP):** `http://localhost:5145`
- **Địa chỉ truy cập giao diện Web (HTTPS):** `https://localhost:7097`
- **Trang đăng nhập:** `http://localhost:5145/account/login`
- **API kiểm tra phiên hiện tại:** `http://localhost:5145/api/me`

---

## 6. Thực thi Bộ Kiểm thử Tự động và Quy trình CI/CD

### 6.1. Kiểm tra Schema tĩnh & EF Core Model Drift
```powershell
python scripts/static_schema_checks.py
dotnet ef migrations has-pending-model-changes --project src/BetaGiaiPhong.Data --startup-project src/BetaGiaiPhong.Data
```

### 6.2. Chạy 173 bài kiểm thử xUnit (Unit + SQL Integration + WebApplicationFactory)
> **Cơ chế an toàn DB kiểm thử (`TestDatabaseFixture`):** Bộ kiểm thử tích hợp yêu cầu biến môi trường `BGP_TEST_ALLOW_DB_CREATE=true` và đọc chuỗi kết nối SQL Server qua biến `BGP_TEST_SQL_MASTER_CONNECTION` để tự động tạo các cơ sở dữ liệu tạm thời độc lập (`BetaGiaiPhong_Test_<guid12>`) gắn nhãn sở hữu `BGP_TestRunOwnershipToken` và tự động dọn sạch (`DROP DATABASE`) sau khi chạy xong.

```powershell
$env:BGP_TEST_ALLOW_DB_CREATE = "true"
$env:BGP_TEST_SQL_MASTER_CONNECTION = "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test BetaGiaiPhong.slnx -c Release
```

### 6.3. Chạy 15 Bộ Kiểm thử Trình duyệt Thực Playwright Chromium E2E (`50` bài kiểm thử)
Playwright E2E (`e2e/global-setup.ts`) tự động tạo một CSDL riêng `BetaGiaiPhong_E2E_<runId>`, khởi chạy tiến trình `BetaGiaiPhong.Web` trên cổng `5199`, chạy toàn bộ 5 tệp spec trên trình duyệt Chromium (`retries: 0`), chạy `Runner verify` kiểm tra 9 bất biến SQL, và tự động xóa CSDL E2E trong `e2e/global-teardown.ts`:

```powershell
Push-Location e2e
npx playwright test
Pop-Location
```

---

## 7. Xử lý Sự cố Thường gặp (Troubleshooting)

| Triệu chứng lỗi | Nguyên nhân | Cách khắc phục |
|---|---|---|
| `[ERROR] Missing cryptographic keys in .NET User Secrets and environment!` khi chạy `01_prepare_dev_session.ps1` | Chưa khởi tạo khóa CSPRNG trong `.NET User Secrets` của `src/BetaGiaiPhong.Web`. | Chạy khối lệnh khởi tạo khóa ở **Mục 3.1** một lần duy nhất, sau đó gọi lại `. .\scripts\01_prepare_dev_session.ps1`. |
| `InvalidOperationException` khi chạy `dotnet test`: từ chối tạo DB kiểm thử | Chưa đặt `$env:BGP_TEST_ALLOW_DB_CREATE = "true"`. | Đặt `$env:BGP_TEST_ALLOW_DB_CREATE = "true"` và `$env:BGP_TEST_SQL_MASTER_CONNECTION` như hướng dẫn ở **Mục 6.2**. |
| `CryptographicException` khi mở trang vé `/orders/{id}/tickets` hoặc tải ảnh `/tickets/{ticketId}/qr` | Khóa `BGP_QR_AES_KEY_B64` của tiến trình Web khác với khóa đã dùng lúc `Runner` phát hành vé trong DB. | Luôn gọi `. .\scripts\01_prepare_dev_session.ps1` trong cùng cửa sổ PowerShell trước khi chạy `Runner` và `Web`. |
| Tạo suất chiếu mới ở Phòng 1 (`P01`) tại `now + 15 phút` báo lỗi chồng lấn lịch chiếu | Trước đó đã chạy `seed-checkin-demo`, lệnh này đã tạo sẵn một suất chiếu ở Phòng 1 tại `now + 15 phút`. | Chọn **Phòng chiếu số 2 (`P02`)** khi tạo suất chiếu mới, hoặc không chạy `seed-checkin-demo` nếu muốn tự tạo suất chiếu ở Phòng 1. |
| Nhân viên quét vé hợp lệ nhưng nhận kết quả `NGOÀI KHUNG GIỜ (OUTSIDE_WINDOW)` | Suất chiếu đang chọn có thời gian bắt đầu cách hiện tại quá 30 phút (`now < start_utc - 30m`) hoặc đã kết thúc quá 15 phút (`now > end_utc + 15m`). | Chạy `dotnet run --project .\src\BetaGiaiPhong.Runner -- seed-checkin-demo` hoặc dùng tài khoản Manager tạo một suất chiếu mới có giờ bắt đầu sau thời điểm hiện tại 10–15 phút. |
| Nhân viên tải tệp ảnh không phải mã QR hoặc tệp ảnh bị hỏng lên `/staff/check-in` | Tệp ảnh không chứa ma trận QR hợp lệ. | Hệ thống hiển thị rõ trạng thái **`"ẢNH QR LỖI"`** (`INVALID_QR_IMAGE`). Bấm `"Quét vé khác"` và tải đúng tệp ảnh PNG mã QR tải từ trang vé của khách hàng. |
