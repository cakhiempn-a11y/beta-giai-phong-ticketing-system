# HƯỚNG DẪN CÀI ĐẶT, CẤU HÌNH VÀ VẬN HÀNH HỆ THỐNG (`INSTALLATION_GUIDE`)

**Đề tài:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Tác giả:** Vũ Văn Khiêm (`11235581`) — Ngành Hệ thống thông tin quản lý (MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương

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

Mở PowerShell tại thư mục làm việc (ví dụ `D:\Projects\BetaGiaiPhong`):

```powershell
# 1. Khôi phục công cụ локаl (dotnet-ef) và các gói NuGet của toàn bộ Solution
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

## 3. Cấu hình Biến Môi trường và Khóa Mật mã (Secrets)

Theo yêu cầu bảo mật `NFR-10`, kho mã nguồn **không lưu cứng (hard-code)** mật khẩu hay khóa mật mã trong `appsettings.json`. Trước khi chạy ứng dụng hoặc công cụ `Runner`, bạn cần thiết lập các biến môi trường (hoặc chạy script tự động ở Mục 4):

| Tên biến môi trường | Ý nghĩa | Giá trị mẫu cho môi trường phát triển cục bộ |
|---|---|---|
| `ConnectionStrings__BetaDb` | Chuỗi kết nối SQL Server cho ứng dụng Web và Runner | `Server=localhost;Database=BetaGiaiPhong_Dev;Trusted_Connection=True;TrustServerCertificate=True;` |
| `BGP_QR_AES_KEY_B64` | Khóa đối xứng **AES-256** (32 byte mã hóa Base64) dùng để niêm phong/giải mã `Tickets.qr_token_cipher` | Sinh ngẫu nhiên 32 byte: `[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 }))` |
| `BGP_MOCK_HMAC_KEY_B64` | Khóa bí mật **HMAC-SHA256** (tối thiểu 32 byte Base64) để ký và xác thực Webhook thanh toán | Sinh ngẫu nhiên 32 byte Base64 |
| `BGP_DEMO_PASSWORD` | Mật khẩu khởi tạo cho 5 tài khoản demo khi chạy `DemoSeeder` | Ví dụ: `BetaDemo@2026!` (tối thiểu 10 ký tự) |
| `BGP_ENABLE_MOCK_PAYMENT_SIMULATOR`| Bật giao diện nút bấm mô phỏng thanh toán trực tiếp trên trang Checkout (`true`/`false`) | `true` (bắt buộc bật khi trình diễn demo) |
| `BGP_ENABLE_EXPIRY_WORKER` | Bật tiến trình nền `ReservationExpiryWorker` tự động thu hồi ghế quá hạn 5 phút | `true` |

> [!CAUTION]
> **CẢNH BÁO QUAN TRỌNG VỀ TÍNH ỔN ĐỊNH CỦA KHÓA `BGP_QR_AES_KEY_B64`:**  
> Cột `Tickets.qr_token_cipher` trong SQL Server được mã hóa bằng thuật toán `AES-256-GCM` với khóa `BGP_QR_AES_KEY_B64` tại thời điểm phát hành vé.  
> - Nếu bạn khởi động lại ứng dụng Web với một khóa `BGP_QR_AES_KEY_B64` **khác** với khóa đã dùng khi tạo vé, ứng dụng sẽ **không thể giải mã** các vé cũ để vẽ ảnh QR PNG (`CryptographicException: The computed authentication tag did not match`).  
> - **Quy tắc bắt buộc khi demo:** Luôn giữ cố định cùng một giá trị `BGP_QR_AES_KEY_B64` trong suốt phiên làm việc (script `scripts/01_prepare_dev_session.ps1` tự động lưu khóa vào `.dev-session.env.json` để tái sử dụng ổn định qua nhiều lần khởi động lại).

---

## 4. Khởi tạo Cơ sở Dữ liệu (EF Core Migrations) và Nạp Dữ liệu Mẫu (Demo Seeding)

### Cách 1: Sử dụng bộ Script PowerShell tự động (Khuyên dùng)

Dự án cung cấp sẵn bộ 3 script tự động hóa tại thư mục `scripts/`:

```powershell
# Bước 1: Sinh và lưu cố định khóa AES/HMAC vào .dev-session.env.json, chạy Migration + Seed danh mục mẫu + Verify 9 bất biến
.\scripts\01_prepare_dev_session.ps1

# Bước 2 (Tùy chọn): Tạo sẵn một suất chiếu nằm ngay trong khung giờ soát vé (StartUtc = hiện tại + 15 phút) phục vụ demo quầy Staff
.\scripts\03_seed_checkin_demo.ps1
```

### Cách 2: Chạy trực tiếp qua CLI `BetaGiaiPhong.Runner`

Nếu muốn tự điều khiển từng bước trên cửa sổ PowerShell hiện tại:

```powershell
# 1. Áp dụng 2 EF Core Migrations (tạo 17 bảng, 22 FK và View dbo.vw_ShowtimeSales)
dotnet run --project src/BetaGiaiPhong.Runner -- migrate

# 2. Nạp dữ liệu mẫu không phá hủy (5 tài khoản, 2 phòng chiếu, sơ đồ ghế, phim và suất chiếu mẫu)
dotnet run --project src/BetaGiaiPhong.Runner -- seed-demo

# 3. Kiểm tra 9 nhóm bất biến toàn vẹn SQL Server
dotnet run --project src/BetaGiaiPhong.Runner -- verify
```

> [!WARNING]
> **Lưu ý bảo vệ dữ liệu vé đang hoạt động:**  
> Lệnh `seed-demo` được thiết kế an toàn (idempotent): chỉ bổ sung danh mục mẫu nếu chưa có mà **không xóa** các đơn hàng/vé đã phát sinh. Tuyệt đối không chạy lệnh `DROP DATABASE` lên cơ sở dữ liệu đang chứa các vé mẫu mà bạn chuẩn bị dùng để trình diễn trước hội đồng.

### Danh sách Tài khoản Demo sau khi Seed

Mật khẩu của toàn bộ các tài khoản dưới đây là giá trị biến `BGP_DEMO_PASSWORD` (mặc định khi chạy `01_prepare_dev_session.ps1` sẽ được in rõ trên màn hình console):

| Vai trò (Role) | Email đăng nhập | Quyền hạn chính |
|---|---|---|
| **`ADMIN`** | `admin@demo.invalid` | Toàn quyền hệ thống và quản lý vận hành rạp |
| **`MANAGER`** | `manager@demo.invalid` | Quản lý phim, xem phòng/sơ đồ ghế, công bố suất chiếu & giá vé, xem Dashboard và Báo cáo doanh thu |
| **`STAFF`** | `staff@demo.invalid` | Quét mã QR soát vé tại quầy (`/staff/check-in`) và xem lịch sử soát vé |
| **`CUSTOMER` #1** | `customer1@demo.invalid`| Đặt ghế, giữ chỗ, thanh toán mô phỏng, hủy đơn chờ và nhận vé điện tử QR |
| **`CUSTOMER` #2** | `customer2@demo.invalid`| Tài khoản khách hàng thứ hai dùng để thử nghiệm tranh chấp ghế đồng thời và chống IDOR |

---

## 5. Khởi chạy Ứng dụng Web (`BetaGiaiPhong.Web`)

Sau khi đã chạy `.\scripts\01_prepare_dev_session.ps1`, khởi động máy chủ Web bằng lệnh:

```powershell
.\scripts\02_run_web_dev.ps1
```

Hoặc chạy trực tiếp bằng `dotnet run` (khi các biến môi trường đã được nạp):

```powershell
dotnet run --project src/BetaGiaiPhong.Web/BetaGiaiPhong.Web.csproj --launch-profile http
```

- **Địa chỉ truy cập giao diện Web:** `http://localhost:5095` (hoặc cổng được cấu hình trong `launchSettings.json` / biến `ASPNETCORE_URLS`).
- **Điểm cuối kiểm tra trạng thái sống (Health Check):** `http://localhost:5095/health` (trả về `200 OK` khi Web và SQL Server sẵn sàng).

---

## 6. Thực thi Bộ Kiểm thử Tự động và Quy trình CI/CD

### 6.1. Kiểm tra Schema tĩnh & EF Core Model Drift
```powershell
python scripts/static_schema_checks.py
dotnet ef migrations has-pending-model-changes --project src/BetaGiaiPhong.Data --startup-project src/BetaGiaiPhong.Data
```

### 6.2. Chạy 166 bài kiểm thử xUnit (Unit + SQL Integration + WebApplicationFactory)
> **Cơ chế an toàn DB kiểm thử (`TestDatabaseFixture`):** Bộ kiểm thử tích hợp yêu cầu biến môi trường `BGP_TEST_ALLOW_DB_CREATE=true` để tự động tạo các cơ sở dữ liệu tạm thời độc lập (`BetaGiaiPhong_Test_<guid12>`) gắn nhãn sở hữu `BGP_TestRunOwnershipToken` và tự động dọn sạch sau khi chạy xong.

```powershell
$env:BGP_TEST_ALLOW_DB_CREATE = "true"
$env:BGP_TEST_MASTER_CONNECTION_STRING = "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test BetaGiaiPhong.slnx -c Release
```

### 6.3. Chạy 5 Bộ Kiểm thử Trình duyệt Thực Playwright Chromium E2E
Playwright E2E sẽ tự động tạo một CSDL riêng `BetaGiaiPhong_E2E_<runId>`, tự khởi chạy tiến trình `BetaGiaiPhong.Web` trên cổng `5188`, chạy toàn bộ 5 tệp spec trên trình duyệt Chromium, chạy `Runner verify` kiểm tra 9 bất biến SQL, và tự động xóa CSDL E2E trong `global-teardown.ts`:

```powershell
Push-Location e2e
npx playwright test
Pop-Location
```

### 6.4. Quy trình GitHub Actions CI (`.github/workflows/ci.yml`)
Mọi sự kiện `push` vào `main` và mọi `pull_request` đều kích hoạt tự động 4 jobs trên GitHub-hosted Ubuntu 24.04 runner:
1. **`build-and-validate`**: Biên dịch Release, kiểm tra `static_schema_checks.py`, kiểm tra `has-pending-model-changes` và quét lỗ hổng NuGet.
2. **`automated-tests`**: Khởi chạy container SQL Server 2022, chạy toàn bộ **166 xUnit tests** và xuất báo cáo độ phủ mã nguồn.
3. **`integrity-checks`**: Chạy `Runner` (`migrate`, `seed-demo`, `smoke`, `race`, `verify`) xác nhận 9/9 bất biến SQL.
4. **`e2e-browser-tests`**: Chạy toàn bộ **5 bộ Playwright Chromium E2E tests**, kiểm tra cổng chất lượng teardown và quét làm sạch artifact trước khi lưu trữ.

---

## 7. Xử lý Sự cố Thường gặp (Troubleshooting)

| Triệu chứng lỗi | Nguyên nhân | Cách khắc phục |
|---|---|---|
| `InvalidOperationException: Refusing to create test database because BGP_TEST_ALLOW_DB_CREATE is not set to 'true'` | Chạy `dotnet test` mà chưa bật cờ cho phép tạo DB kiểm thử tạm thời. | Đặt `$env:BGP_TEST_ALLOW_DB_CREATE="true"` trước khi chạy `dotnet test`. |
| `CryptographicException` khi mở trang vé `/orders/{id}/tickets` hoặc tải ảnh QR PNG | Khóa `BGP_QR_AES_KEY_B64` hiện tại khác với khóa đã dùng lúc phát hành vé trong DB. | Dùng `.\scripts\02_run_web_dev.ps1` để tự động nạp lại đúng khóa từ `.dev-session.env.json`, hoặc tạo đơn hàng mới với khóa hiện tại. |
| Nhân viên quét vé hợp lệ nhưng nhận kết quả `NGOÀI GIỜ SOÁT VÉ (OUTSIDE_WINDOW)` | Suất chiếu đang chọn có thời gian bắt đầu cách hiện tại quá 30 phút (`now < start_utc - 30m`) hoặc đã kết thúc quá 15 phút (`now > end_utc + 15m`). | Chạy `.\scripts\03_seed_checkin_demo.ps1` hoặc vào tài khoản Manager tạo một suất chiếu mới có giờ bắt đầu sau thời điểm hiện tại 10–15 phút. |
| Không thấy nút mô phỏng thanh toán (`#btnPaySuccess` / `#btnPayFailed`) trên trang Checkout | Biến cấu hình `BGP_ENABLE_MOCK_PAYMENT_SIMULATOR` đang tắt (`false`). | Đặt `$env:BGP_ENABLE_MOCK_PAYMENT_SIMULATOR="true"` và khởi động lại `BetaGiaiPhong.Web`. |
| Trình duyệt trên điện thoại báo không bật được Camera tại `/staff/check-in` | Truy cập qua địa chỉ HTTP LAN (`http://192.168.x.x`) không phải ngữ cảnh an toàn (`Secure Context`). | Dùng tính năng **Tải ảnh QR PNG** (`#qrFileInput`), hoặc dùng Chrome USB Port Forwarding (`localhost`), hoặc chạy Kestrel với `--launch-profile https`. |
