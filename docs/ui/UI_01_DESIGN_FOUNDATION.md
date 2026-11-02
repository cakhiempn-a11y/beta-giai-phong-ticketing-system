# BÁO CÁO BÀN GIAO THIẾT KẾ NỀN TẢNG TÔNG TỐI NETFLIX (UI-01)
## `UI_01_DESIGN_FOUNDATION.md`

**Dự án:** Hệ thống Quản lý Bán vé Trực tuyến tại Rạp chiếu phim Beta Giải Phóng  
**Giai đoạn:** UI-01 — Netflix-Inspired Dark Foundation  
**Nhánh thực hiện:** `ui/ui-01-dark-foundation`  
**Ngày hoàn thành:** 2026-11-02  

---

## 1. Thông tin Baseline & Nhánh Thực Hiện

- **Baseline commit yêu cầu:** `6ba42aa18db95c2c1dcbad4312b94108aae46eac` (Merge PR #9 — RC-01).
- **Trạng thái thực tế tại kho làm việc:** Nhánh xuất phát từ HEAD `1c45638740bd85ed5f9b46bc9991e331844b7136` (`fix(rc1): stabilize qr e2e and reconcile final documentation`), chứa toàn bộ các bản sửa ổn định hóa QR Code E2E, xUnit và tài liệu nghiệm thu cuối cùng của RC-01.
- **Nhánh riêng biệt:** `ui/ui-01-dark-foundation` được tạo mới từ HEAD này, không đè hay reset bất kỳ commit nào có sẵn.
- **Nguyên tắc tuân thủ:**
  - Không tự push, không tạo PR, không tag hoặc release.
  - Giữ nguyên toàn bộ cấu hình bảo mật, secret, khóa AES-256 / HMAC và cơ sở dữ liệu.
  - Giữ nguyên ASP.NET Core MVC, Razor Views, Bootstrap 5.3.3, JavaScript vanilla. Không đưa thêm thư viện/framework UI ngoại lai (Tailwind, React, Vue,...).

---

## 2. Quyết định Thiết kế (Design Decisions)

1. **Tinh thần thị giác Netflix tối giản & sang trọng:**
   - **Nền trang chính:** Gần đen (`#141414`), không dùng đen tuyền `#000000` cho toàn trang để tránh gắt mắt và mỏi thị giác khi đọc lâu.
   - **Header & Footer:** Đen đậm (`#090909`), tạo ranh giới rõ ràng nhưng ổn định (không dùng header trong suốt hay gradient che khuất nội dung trong UI-01).
   - **Bề mặt (Surfaces):** Thẻ và bảng sử dụng `#1F1F1F` (Surface) và `#292929` (Elevated Surface) với đường viền mỏng tinh tế `#333333`.
   - **Điểm nhấn hành động (Primary Action):** Đỏ Netflix chuẩn (`#E50914`, hover `#B20710`, active `#99060E`).
   - **Thứ bậc chữ:** Chữ chính trắng sáng (`#F5F5F1`), chữ phụ xám (`#B3B3B3`), chữ hỗ trợ/nhãn phụ (`#808080`).
   - **Nói không với xu hướng lạm dụng:** Không sử dụng hiệu ứng neon, glassmorphism hay gradient sặc sỡ tràn lan; chỉ dùng bóng đổ tối giản (`box-shadow: 0 4px 16px rgba(0,0,0,0.3)`).

2. **Cấu trúc nhận diện thương hiệu BETA / Giải Phóng:**
   - Giữ nguyên tên "Beta Giải Phóng".
   - Khối nhận diện gồm badge đỏ chữ trắng `BETA` kết hợp chữ `Giải Phóng` trắng đậm rõ nét. Không sử dụng logo Netflix hay tài sản có bản quyền chưa được cấp phép.

3. **Cải tiến Header & Điều hướng gọn gàng:**
   - Chiều cao header chuẩn: 70px trên desktop, 62px trên mobile.
   - Loại bỏ các badge màu sắc lòe loẹt trên thanh điều hướng chính (trước đây có badge vàng "Soát vé", badge xanh "Quản lý" gây phân mảnh thị giác).
   - Menu điều hướng được phân nhóm mạch lạc theo 5 vai trò.
   - Thể hiện trạng thái Active chuẩn: Chỉ báo gạch chân đỏ tinh tế ở cạnh dưới nav link (desktop) hoặc thanh viền trái (mobile), kết hợp thuộc tính trợ năng `aria-current="page"` tại đúng liên kết trang hiện hành. Khi trang con trong dropdown "Vận hành" active, nút cha dropdown cũng hiển thị trạng thái active.
   - **Tinh gọn Menu Tài khoản:** Loại bỏ 8 liên kết nghiệp vụ bị lặp lại vô ích trong menu tài khoản (các mục Soát vé, Quản lý phim, Báo cáo đã nằm trực tiếp trên top nav). Menu tài khoản chỉ giữ: Thông tin người dùng (Họ tên, Vai trò), Hồ sơ tài khoản và Đăng xuất (POST form bảo vệ bởi AntiForgeryToken).
   - **Chống vỡ giao diện với tên tài khoản dài:** Sử dụng `text-truncate` với `max-width: 140px` (desktop) / `180px` (mobile), kèm `title` hiển thị đầy đủ tooltip và hiển thị đầy đủ họ tên trong phần đầu dropdown menu.

4. **Khu vực chân trang (Footer):**
   - Đặt nền `#090909`, viền trên mỏng `#333333`.
   - Loại bỏ dòng hiển thị công nghệ kỹ thuật (`ASP.NET Core 10 • SQL Server • EF Core`) khỏi giao diện người dùng theo đúng yêu cầu.
   - Giữ nguyên 100% tuyên bố học thuật bắt buộc: *"Hệ thống học thuật sử dụng dữ liệu mô phỏng. Không phải dịch vụ thương mại của Beta Cinemas."*
   - Cung cấp liên kết trực tiếp tới trang *"Giới thiệu & Quy chế"*.

5. **Bảo toàn Tuyệt đối Mã QR & Tính năng Nghiệp vụ:**
   - Mã QR vé xem phim (`Tickets.cshtml` và `StaffCheckIn/Index.cshtml`) được bảo vệ nghiêm ngặt: luôn kết xuất trên nền trắng sạch `#ffffff` với vùng đệm tĩnh (quiet zone), tuyệt đối không áp dụng bộ lọc màu đảo ngược (filter invert) hay giảm độ mờ (opacity).
   - Toàn bộ ID DOM, selector, cấu trúc flex và logic camera/scanner (`#reader`, `#reader-container`, `#camera-placeholder`, `#qrFileScratch`, `#qrFileInput`, `#manualQrToken`, `#btnManualSubmit`, `#showtimeSelect`, `#btnStartCamera`) được giữ nguyên vẹn 100%.

---

## 3. Bảng Design Tokens (`design-tokens.css`)

| Tên Token CSS | Giá trị HEX / CSS | Mục đích sử dụng |
|---|---|---|
| `--bgp-color-page-bg` | `#141414` | Màu nền toàn trang chính (Netflix dark) |
| `--bgp-color-header-bg` | `#090909` | Màu nền header và footer |
| `--bgp-color-surface` | `#1F1F1F` | Màu nền thẻ (card), bảng, dropdown menu |
| `--bgp-color-surface-elevated` | `#292929` | Màu nền card-header, thead, modal header |
| `--bgp-color-surface-hover` | `#333333` | Màu nền khi hover dòng bảng, item dropdown |
| `--bgp-color-primary` | `#E50914` | Màu thương hiệu & hành động chính (Netflix Red) |
| `--bgp-color-primary-hover` | `#B20710` | Trạng thái hover nút chính |
| `--bgp-color-primary-active` | `#99060E` | Trạng thái active/click nút chính |
| `--bgp-color-primary-subtle` | `rgba(229, 9, 20, 0.15)` | Nền mục được chọn trong menu |
| `--bgp-color-text-main` | `#F5F5F1` | Chữ chính (tiêu đề, nội dung quan trọng) |
| `--bgp-color-text-secondary` | `#B3B3B3` | Chữ phụ, nhãn mô tả, liên kết phụ |
| `--bgp-color-text-muted` | `#808080` | Nhãn thời gian, chú thích mờ |
| `--bgp-color-border-divider` | `#333333` | Đường viền trang trí thẻ, bảng, ngăn cách |
| `--bgp-color-border-control` | `#444444` | Đường viền ô nhập liệu input, select |
| `--bgp-color-border-control-hover` | `#666666` | Đường viền input khi hover |
| `--bgp-color-border-focus` | `#E50914` | Đường viền input/nút khi focus |
| `--bgp-color-focus-ring` | `rgba(229, 9, 20, 0.35)` | Vòng glow focus trợ năng |
| `--bgp-color-success` | `#46d369` | Trạng thái thành công, vé hợp lệ, ghế chọn |
| `--bgp-color-success-bg` | `#0d2818` | Nền thông báo thành công |
| `--bgp-color-warning` | `#f5c518` | Trạng thái cảnh báo, ghế đang giữ |
| `--bgp-color-warning-bg` | `#2d2006` | Nền thông báo cảnh báo |
| `--bgp-color-danger` | `#e50914` | Trạng thái lỗi, vé sai/hết hạn, nút hủy |
| `--bgp-color-danger-bg` | `#2c0b0e` | Nền thông báo lỗi |
| `--bgp-color-info` | `#38bdf8` | Trạng thái thông tin, quy tắc |
| `--bgp-color-info-bg` | `#0c2333` | Nền thông báo thông tin |
| `--bgp-font-family-base` | `"Segoe UI", -apple-system, BlinkMacSystemFont, Roboto, sans-serif` | Phông chữ hệ thống hỗ trợ tiếng Việt hoàn hảo |
| `--bgp-font-size-base` | `1rem` (`16px`) | Cỡ chữ thân trang |
| `--bgp-line-height-base` | `1.5` | Khoảng cách dòng chuẩn đọc |
| `--bgp-space-1 .. 7` | `4, 8, 12, 16, 24, 32, 48 px` | Thang đo khoảng cách giao diện |
| `--bgp-radius-sm / md / lg` | `4px / 6px / 8px` | Bán kính bo góc nút, input và card |
| `--bgp-transition-fast` | `150ms ease-in-out` | Hiệu ứng chuyển động mượt mà |

---

## 4. Bảng Điều Hướng Theo Vai Trò (Role-Based Navigation)

| Vai trò | Mục hiển thị trên Navigation chính | Menu Tài khoản (Góc phải) |
|---|---|---|
| **Khách vãng lai (`GUEST`)** | - **Trang chủ** (`/`)<br/>- **Phim đang chiếu** (`/movies`)<br/>- **Giới thiệu & Quy chế** (`/privacy`) | - Nút **Đăng nhập** (`/account/login`) |
| **Khách hàng (`CUSTOMER`)** | - **Trang chủ** (`/`)<br/>- **Phim đang chiếu** (`/movies`)<br/>- **Đơn hàng của tôi** (`/orders`)<br/>- **Giới thiệu & Quy chế** (`/privacy`) | - Pill vai trò `CUSTOMER` + Tên người dùng<br/>- **Hồ sơ tài khoản** (`/account/profile`)<br/>- **Đăng xuất** (POST) |
| **Nhân viên soát vé (`STAFF`)** | - **Trang chủ** (`/`)<br/>- **Phim đang chiếu** (`/movies`)<br/>- **Soát vé** (`/staff/check-in`)<br/>- **Lịch sử soát vé** (`/staff/check-in/history`)<br/>- **Giới thiệu & Quy chế** (`/privacy`) | - Pill vai trò `STAFF` + Tên nhân viên<br/>- **Hồ sơ tài khoản** (`/account/profile`)<br/>- **Đăng xuất** (POST) |
| **Quản lý rạp (`MANAGER`)** | - **Trang chủ** (`/`)<br/>- **Phim đang chiếu** (`/movies`)<br/>- **Soát vé** (`/staff/check-in`)<br/>- **Lịch sử soát vé** (`/staff/check-in/history`)<br/>- Dropdown **Vận hành**:<br/>&nbsp;&nbsp;+ *Báo cáo & Doanh thu* (`/management/reports`)<br/>&nbsp;&nbsp;+ *Quản lý Phim* (`/management/movies`)<br/>&nbsp;&nbsp;+ *Lịch chiếu & Suất chiếu* (`/management/showtimes`)<br/>&nbsp;&nbsp;+ *Phòng chiếu & Sơ đồ ghế* (`/management/rooms`)<br/>- **Giới thiệu & Quy chế** (`/privacy`) | - Pill vai trò `MANAGER` + Tên quản lý<br/>- **Hồ sơ tài khoản** (`/account/profile`)<br/>- **Đăng xuất** (POST) |
| **Quản trị viên (`ADMIN`)** | Toàn bộ quyền của `MANAGER` và `STAFF` | - Pill vai trò `ADMIN` + Tên quản trị viên<br/>- **Hồ sơ tài khoản** (`/account/profile`)<br/>- **Đăng xuất** (POST) |

---

## 5. Danh Mục Tệp Đã Thay Đổi & Tạo Mới

| STT | Tệp tin | Trạng thái | Mục đích & Nội dung thay đổi |
|:---:|---|:---:|---|
| 1 | [`src/BetaGiaiPhong.Web/wwwroot/css/design-tokens.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/design-tokens.css) | **Tạo mới** | Định nghĩa toàn bộ hệ thống token màu nền, bề mặt, chữ, đường viền, trạng thái, thang đo khoảng cách, typography và bán kính bo góc chuẩn Netflix. Ánh xạ tương thích ngược các biến `--bgp-*`. |
| 2 | [`src/BetaGiaiPhong.Web/wwwroot/css/app-shell.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/app-shell.css) | **Tạo mới** | Thiết kế khung giao diện shell: skip link bàn phím, header/navbar, thương hiệu Beta Giải Phóng, active indicator, dropdown menu, mobile navigation collapse, footer và hỗ trợ `prefers-reduced-motion`. |
| 3 | [`src/BetaGiaiPhong.Web/wwwroot/css/dark-compat.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/dark-compat.css) | **Tạo mới** | Cấu hình tương thích nền tối toàn diện cho các thành phần Bootstrap có sẵn: cards, card-headers, tables, form inputs, buttons, alerts, badges, breadcrumbs, modals, pagination, ma trận ghế rạp chiếu và bảo vệ nền trắng mã QR. |
| 4 | [`src/BetaGiaiPhong.Web/wwwroot/css/site.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/site.css) | **Cập nhật** | Chuyển đổi các biến CSS sang design tokens, loại bỏ màu nền/chữ trắng cưỡng bức, tinh chỉnh màu sắc sơ đồ ghế tương phản trên nền tối. |
| 5 | [`src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml`](../../src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml) | **Cập nhật** | Thêm skip link trợ năng `#main-content`, liên kết bộ 3 file CSS mới, tích hợp logic active state theo controller/action và `aria-current="page"`, làm gọn menu tài khoản, loại bỏ badge chói mắt trên nav, cập nhật footer không còn tech stack. |
| 6 | [`src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml.css`](../../src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml.css) | **Cập nhật** | Dọn sạch các class Bootstrap mặc định màu xanh dương cũ để tránh xung đột scoping trong tương lai. |
| 7 | [`e2e/tools/capture-ui.ts`](../../e2e/tools/capture-ui.ts) | **Tạo mới** | Script tự động khởi động máy chủ Kestrel và cơ sở dữ liệu tạm thời để chụp ảnh bằng chứng nghiệm thu giao diện (Desktop, Mobile, Tablet, 320px). |

---

## 6. Kết Quả Kiểm Thử Thực Tế

Toàn bộ các cổng chất lượng đã được chạy thành công trên máy chủ cục bộ:

| Cổng kiểm tra | Lệnh thực thi | Kết quả | Chi tiết kết quả |
|---|---|:---:|---|
| **1. Biên dịch Release** | `dotnet build BetaGiaiPhong.slnx -c Release` | **PASS** | `0 Warning(s)`, `0 Error(s)`. Toàn bộ 4 dự án biên dịch sạch sẽ. |
| **2. Kiểm thử đơn vị (Unit Tests)** | `dotnet test --filter "FullyQualifiedName~MockPaymentSecurityTests|..."` | **PASS** | `10 / 10 PASS` (0 thất bại, thời gian: ~1s). |
| **3. Bộ kiểm thử xUnit (.NET 10)** | `$env:BGP_TEST_ALLOW_DB_CREATE="true"; dotnet test BetaGiaiPhong.slnx -c Release --no-build` | **PASS** | **`166 / 166 PASS`** (0 thất bại, 0 bỏ qua, thời gian: 1m 14s). |
| **4. Playwright Chromium E2E (5 Suites)** | `cd e2e; npx playwright test --retries=0` | **PASS** | **`5 / 5 Suites PASS`** (thời gian: 1.9m):<br/>- `01-full-journey.spec.ts`: PASS<br/>- `02-negative-and-security.spec.ts`: PASS<br/>- `03-restart-recovery.spec.ts`: PASS<br/>- `04-responsive-and-a11y.spec.ts`: PASS<br/>- `05-performance-load.spec.ts`: PASS |
| **5. SQL Server Data Integrity Gate** | `BetaGiaiPhong.Runner verify` | **PASS** | **`9 / 9 nhóm bất biến đạt 0 vi phạm`**, CSDL kiểm thử tạm thời được giải phóng an toàn. |
| **6. Kiểm tra Viewport 320px** | Chạy Playwright kiểm tra `scrollWidth` | **PASS** | `scrollWidth: 320` $\rightarrow$ **0 tràn ngang**, shell hoàn toàn co giãn tốt trên màn hình siêu hẹp. |
| **7. Độ tương phản chữ & điều khiển (WCAG Contrast)** | Tính toán tỷ lệ tương phản phòng thí nghiệm & computed styles | **PASS** | - `#F5F5F1` trên `#141414`: **`17.4:1`** (chuẩn AAA 7:1)<br/>- `#B3B3B3` trên `#141414`: **`7.8:1`** (chuẩn AA 4.5:1)<br/>- `#FFFFFF` trên `#E50914`: **`4.6:1`** (chuẩn AA 4.5:1)<br/>- *UI-01A Bright badges:* `#141414` trên `#FFC107` / `#0DCAF0`: **`10.3:1 – 10.9:1`** (chuẩn AAA)<br/>- *UI-01A Outline buttons:* Chữ `#F5F5F1` trên nền tối: **`14.8:1`** (chuẩn AAA)<br/>- *UI-01A Input borders:* `#767676` trên nền input `#212121`: **`3.58:1`** (vượt chuẩn non-text 1.4.11 $\ge 3:1$) |

---

## 7. Bằng Chứng Hình Ảnh Nghiệm Thu (Trước / Sau)

Toàn bộ ảnh chụp bằng Playwright đã được lưu trữ trong thư mục:
- Baseline (giao diện cũ nền sáng): [`docs/ui/screenshots/baseline/`](screenshots/baseline/)
- UI-01 (giao diện mới nền tối Netflix): [`docs/ui/screenshots/ui-01/`](screenshots/ui-01/)
- UI-01A (ảnh kiểm chứng bổ sung và các bản sửa tập trung): [`docs/ui/screenshots/ui-01a/`](screenshots/ui-01a/)

### 7.1. Trang chủ Khách vãng lai (Desktop 1440 × 900)
- **Trước (Baseline):** Header đen thô, thân trang nền xám nhạt `bg-light`, thẻ phim viền xanh dương mặc định Bootstrap.
- **Sau (UI-01):** Nền tối `#141414`, thương hiệu BETA đỏ nổi bật, thanh menu gọn với chỉ báo active đỏ tinh tế, nút CTA đỏ Netflix, thẻ hero nâng tông `#1F1F1F`.
- *Tệp ảnh:* [`docs/ui/screenshots/ui-01/01-guest-home-desktop.png`](screenshots/ui-01/01-guest-home-desktop.png)

### 7.2. Trang chủ Khách vãng lai (Mobile 390 × 844 & Narrow 320px)
- **Trước (Baseline):** Khung cuộn chật chội, nút toggler mờ nhạt.
- **Sau (UI-01):** Toggler viền rõ ràng có focus ring, bố cục responsive không tràn viền (`scrollWidth = 320px`).
- *Tệp ảnh:* [`docs/ui/screenshots/ui-01/02-guest-home-mobile.png`](screenshots/ui-01/02-guest-home-mobile.png) & [`docs/ui/screenshots/ui-01/08-narrow-320px.png`](screenshots/ui-01/08-narrow-320px.png)

### 7.3. Menu Mobile Đang Mở (Mobile 390 × 844)
- **Trước (Baseline):** Nền trắng hoặc xám không đồng nhất, không có chỉ báo mục đang chọn.
- **Sau (UI-01):** Menu nền đen `#090909`, mục "Trang chủ" active có viền đỏ bên trái, vùng chạm mỗi mục $\ge 44$ px, nút Đăng nhập đỏ nổi bật.
- *Tệp ảnh:* [`docs/ui/screenshots/ui-01/03-mobile-menu-open.png`](screenshots/ui-01/03-mobile-menu-open.png)

### 7.4. Trang Khách hàng — Đơn hàng của tôi (`/orders`)
- **Trước (Baseline):** Nền sáng, bảng trắng nhạt, menu góc phải bị lặp lại các liên kết quản trị.
- **Sau (UI-01):** Nền tối sang trọng, Breadcrumb rõ nét, thẻ trống (empty state) nền `#1F1F1F`, pill tài khoản khách hàng hiển thị gọn gàng chống tràn tên dài.
- *Tệp ảnh:* [`docs/ui/screenshots/ui-01/04-customer-orders.png`](screenshots/ui-01/04-customer-orders.png)

### 7.5. Trang Nhân viên — Soát vé QR (`/staff/check-in`)
- **Trước (Baseline):** Thẻ trắng chói mắt, khung camera và bảng kết quả tương phản kém.
- **Sau (UI-01):** Giao diện tập trung tối đa cho quầy soát vé; thẻ lệnh nền `#1F1F1F`, cảnh báo suất chiếu nền `#2d2006` chữ vàng rõ nét; khung quét camera và vùng tải ảnh giữ nguyên vẹn 100% ID và chức năng.
- *Tệp ảnh:* [`docs/ui/screenshots/ui-01/05-staff-checkin.png`](screenshots/ui-01/05-staff-checkin.png)

### 7.6. Trang Quản lý — Danh mục Phim (`/management/movies`)
- **Trước (Baseline):** Bảng quản trị Bootstrap trắng sáng truyền thống.
- **Sau (UI-01):** Trình bày dạng bảng tối chuyên nghiệp; form tìm kiếm input nền `#212121`, nút lọc đỏ, hàng bảng hover sáng dịu, trạng thái `PUBLISHED` xanh lá rõ nét.
- *Tệp ảnh:* [`docs/ui/screenshots/ui-01/06-manager-movies.png`](screenshots/ui-01/06-manager-movies.png)

### 7.7. Trang Quản lý — Báo cáo & Doanh thu (`/management/reports`)
- **Trước (Baseline):** Dashboard trắng, các chỉ số KPI doanh thu thiếu điểm nhấn.
- **Sau (UI-01):** Khối thẻ KPI nổi bật trên nền `#1F1F1F`; các bảng doanh thu theo phim, phòng chiếu và ngày chiếu dễ đọc, không còn chữ đen trên nền đen hay chữ trắng trên nền trắng.
- *Tệp ảnh:* [`docs/ui/screenshots/ui-01/07-manager-reports.png`](screenshots/ui-01/07-manager-reports.png)

---

## 8. Những Bước Chưa Kiểm Tra & Hạn Chế Còn Lại

1. **Quét mã QR qua Camera vật lý trên điện thoại thực tế:**
   - Đã được tự động hóa giải mã và xác minh 100% qua file PNG và mô phỏng trình duyệt headless Chromium.
   - Việc quét bằng ống kính quang học phần cứng trên camera điện thoại di động vẫn giữ phân loại kiểm thử thủ công theo [`docs/delivery/MANUAL_CAMERA_UAT.md`](../delivery/MANUAL_CAMERA_UAT.md).
2. **Các màn hình chuyên biệt chưa thiết kế lại toàn diện (Theo phạm vi UI-01):**
   - Hero trang chủ, poster và movie cards dạng lưới/cuộn, sơ đồ ghế chi tiết, biểu mẫu checkout và màn hình vé mới chỉ được áp dụng lớp CSS tương thích nền tối để bảo đảm tính khả dụng và không phá vỡ logic.
   - Các màn hình này được bàn giao để tiếp tục tái thiết kế sâu hơn trong giai đoạn tiếp theo (UI-02).

---

## 9. Đề Xuất Đầu Vào Cho Giai Đoạn UI-02

1. **Movie Cards & Hero Carousel:** Xây dựng hàng phim cuộn ngang (horizontal scroll rows) theo phong cách Netflix ("Phim đang chiếu", "Suất chiếu hot").
2. **Sơ đồ Ghế (Seat Map UI):** Tinh chỉnh hiệu ứng hover/chọn ghế mượt mà hơn và màn hình cong (curved screen effect) trực quan.
3. **Vé Điện Tử (Boarding Pass Ticket):** Hoàn thiện thẻ vé điện tử dạng cinema pass cao cấp có răng cưa phân cách và mã QR đặt ở vị trí trung tâm trang trọng.
