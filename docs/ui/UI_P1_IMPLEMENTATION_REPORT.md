# BÁO CÁO BÀN GIAO GIAI ĐOẠN 1 (UI-P1)
## Hệ Thống Thiết Kế Điện Ảnh Chuẩn Netflix & Kiến Trúc Dual-Shell (`UI_P1_IMPLEMENTATION_REPORT.md`)

**Dự án:** Hệ thống Quản lý Bán vé Trực tuyến tại Rạp chiếu phim Beta Giải Phóng  
**Giai đoạn:** `UI-P1` — Netflix-Cinema Design System Foundation & Dual-Shell Architecture  
**Nhánh thực hiện:** `ui/p1-cinema-design-system-dual-shell`  
**Tài liệu quy chiếu:** [`NETFLIX_UI_MASTER_PLAN.md`](NETFLIX_UI_MASTER_PLAN.md) • [`FRONTEND_ARCHITECTURE_DECISION.md`](FRONTEND_ARCHITECTURE_DECISION.md) • [`REAL_MOVIE_CONTENT_PLAN.md`](REAL_MOVIE_CONTENT_PLAN.md)

---

## 1. Thông Tin Baseline & Phạm Vi Giai Đoạn `UI-P1`

- **Baseline xuất phát (`origin/main`):** `4c0a38301bfae3b99f3bf7f16c88e50e0b920453` (`Merge pull request #12 — UI-02B: Cinematic Movie Catalogue & Movie Details`).
- **Nhánh triển khai độc lập:** `ui/p1-cinema-design-system-dual-shell` (giữ nguyên toàn bộ tài liệu kế hoạch và prototype trong `docs/ui/`).
- **Tuân thủ nghiêm ngặt ranh giới kiến trúc:**
  - **Không sửa đổi** schema SQL Server, EF Core Migrations, khóa mật mã `AES-256-GCM` / `HMAC-SHA256`, hoặc bất kỳ logic nghiệp vụ backend nào.
  - **Không can thiệp sớm** vào nội dung bên trong `Home/Index`, `Movies/*`, `Showtimes/Seats`, `Orders/*`, `StaffCheckIn/*`, `Management/*` (được lên lịch tuần tự tại các giai đoạn `P2` → `P7`).
  - **Không đụng chạm** cơ sở dữ liệu vận hành; mọi kiểm thử xUnit, Playwright E2E, `Runner verify` và công cụ chụp ảnh đều khởi tạo và tự hủy cơ sở dữ liệu tạm (`BetaGiaiPhong_Test_*`, `BetaGiaiPhong_E2E_*`) kèm kiểm tra `OwnershipToken`.

---

## 2. Giải Pháp Kiến Trúc Cốt Lõi Đã Triển Khai

### 2.1. Hệ thống Phông chữ Tiếng Việt Cục bộ (`wwwroot/fonts/`)
- Đóng gói cục bộ **21 tệp `.woff2`** phân tách dải Unicode (`vietnamese`, `latin-ext`, `latin`) kèm giấy phép mã nguồn mở [`OFL-LICENSE.txt`](../../src/BetaGiaiPhong.Web/wwwroot/fonts/OFL-LICENSE.txt), triệt tiêu hoàn toàn phụ thuộc CDN ngoại mạng khi chạy offline hoặc trong môi trường CI cô lập:
  - **`Be Vietnam Pro`** (`400, 500, 600, 700, 800`): Thiết kế chuyên biệt cho dấu thanh tiếng Việt ở cỡ chữ poster/hero lớn (`clamp(2rem, 4.4vw, 3.75rem)`), không bị cắt dấu kép (`ệ`, `ẫ`, `ử`).
  - **`JetBrains Mono`** (`500, 700` + `font-variant-numeric: tabular-nums`): Hiển thị đồng hồ đếm ngược giữ ghế, khung giờ chiếu, giá vé VND, mã đơn và `Correlation ID`.

### 2.2. Kiến trúc CSS Điện ảnh Độc lập (`wwwroot/css/cinema/`)
Hệ thống CSS mới được tổ chức thành 4 lớp rõ ràng, không dùng `!important` tràn lan lên Bootstrap:
1. [`01-tokens.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/01-tokens.css): Thang màu Obsidian 4 lớp (`#0A0A0C` canvas → `#111114` section → `#16161B` card → `#1E1E25` elevated/modal → `#272732` hover), đỏ thương hiệu `#E50914`, vàng điểm đánh giá/nhấn `#F5C518`, bộ token ngữ nghĩa đạt tương phản WCAG AA/AAA, thang đo chữ linh hoạt (`clamp()`), khoảng cách, bán kính bo góc và đường cong chuyển động (`--bgp-ease-out`). Đồng thời ánh xạ ngược các biến `--bgp-color-*` để bảo đảm tương thích mượt cho các trang chờ nâng cấp.
2. [`02-base.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/02-base.css): Khai báo `@font-face` cục bộ (`font-display: swap`), chuẩn hóa `box-sizing`, `scroll-padding-top` bù chiều cao header cố định, vòng focus kép `:focus-visible` (`0 0 0 2px #0A0A0C, 0 0 0 4px #E50914`), skip link bàn phím `#main-content`, hệ container linh hoạt và quy tắc `@media (prefers-reduced-motion: reduce)`.
3. [`03-shell-cinema.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/03-shell-cinema.css): Giao diện khung khách hàng `_CinemaLayout`:
   - Header cố định `68px` (desktop) / `60px` (mobile) chuyển mượt từ nền phủ gradient trong suốt trên đỉnh trang chủ sang nền kính mờ tối đặc `#0A0A0C` (`backdrop-filter: blur(14px)`) khi `window.scrollY > 32px` hoặc trên các trang con không có Hero.
   - Ô tìm kiếm phim trực tiếp trên Header (`GET /movies?q=...`) giữ nguyên từ khóa đang lọc.
   - Nhãn thông báo học thuật tinh tế (`Mô phỏng học thuật`) và menu tài khoản không phụ thuộc Bootstrap JS.
   - Ngăn kéo điều hướng di động (Mobile Slide-Over Drawer) có kích thước chạm tối thiểu `44×44px`, khóa cuộn trang nền và bẫy trả tiêu điểm bàn phím khi đóng bằng `Escape`.
   - Chân trang ghi nhận đầy đủ tuyên bố đề án học thuật MIS và xuất xứ dữ liệu mở (`Wikidata / Wikimedia Commons`, `IMDb Non-Commercial Datasets`, `TMDB API`).
4. [`03-shell-ops.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/03-shell-ops.css): Giao diện khung tác nghiệp nội bộ `_OperationsLayout` với thanh bên cố định `248px` trên desktop, thanh trạng thái ca trực/vai trò trên đỉnh (`STAFF`, `MANAGER`, `ADMIN`) và ngăn kéo di động `>= 44×44px`.
5. [`04-components.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/04-components.css): Bộ thành phần nguyên tử `.bgp-*` độc lập Bootstrap (`.bgp-btn`, `.bgp-icon-btn`, `.bgp-badge`, `.bgp-age-badge`, `.bgp-input`, `.bgp-select`, `.bgp-card`, `.bgp-panel`, `.bgp-dialog`, `.bgp-table`, `.bgp-skeleton`, `.bgp-empty-box`, `.bgp-toast`, `.bgp-alert`) cùng bố cục các trang hệ thống tĩnh đã tách hoàn toàn khỏi Bootstrap.

### 2.3. Kiến trúc Dual-Shell & Cờ Chuyển tiếp `ViewData["UseLegacyBootstrap"]`
- [`Views/_ViewStart.cshtml`](../../src/BetaGiaiPhong.Web/Views/_ViewStart.cshtml) tự động định tuyến theo Controller:
  - `StaffCheckIn` và `Management` → [`_OperationsLayout.cshtml`](../../src/BetaGiaiPhong.Web/Views/Shared/_OperationsLayout.cshtml).
  - Các Controller hướng khách hàng (`Home`, `Movies`, `Showtimes`, `Orders`, `Account`) → [`_CinemaLayout.cshtml`](../../src/BetaGiaiPhong.Web/Views/Shared/_CinemaLayout.cshtml).
- **Cơ chế chuyển tiếp an toàn từng giai đoạn (`UseLegacyBootstrap`):**
  - Các view đã nâng cấp trong `UI-P1` (`Home/Privacy`, `Account/AccessDenied`, `Home/AccessDenied`, `Shared/Error`) đặt `ViewData["UseLegacyBootstrap"] = false` → trình duyệt **không tải** `bootstrap.min.css`, `dark-compat.css`, `jquery.min.js` hay `bootstrap.bundle.min.js`, kết xuất thuần túy trên nền `#0A0A0C` của hệ thống mới.
  - Các view chờ nâng cấp ở `UI-P3` → `UI-P7` mặc định giữ `UseLegacyBootstrap = true`, tiếp tục hoạt động chính xác 100% bên trong khung Dual-Shell mới mà không gãy bất kỳ bài kiểm thử E2E hiện hữu nào.

---

## 3. Ma Trận Phân Tách Điều Hướng Theo Vai Trò (Dual-Shell Navigation)

| Vai trò | Trên `_CinemaLayout` (Khám phá & Đặt vé) | Chuyển ngữ cảnh sang Vận hành | Trên `_OperationsLayout` (Tác nghiệp nội bộ) |
|---|---|---|---|
| **`GUEST`** | `Trang chủ` • `Danh mục phim` • `Giới thiệu & Quy chế` + Tìm kiếm + Nút `Đăng nhập` | *Không hiển thị* | Bị chặn `401/302` về trang đăng nhập |
| **`CUSTOMER`** | `Trang chủ` • `Danh mục phim` • `Đơn hàng của tôi` • `Giới thiệu & Quy chế` + Menu `CUSTOMER` | *Không hiển thị* | Bị chặn `403` (`AccessDenied`) |
| **`STAFF`** | Chỉ hiển thị liên kết khách hàng (không rò rỉ menu nội bộ lên thanh nav) | **1 nút duy nhất:** `Console Vận hành →` (`/staff/check-in`) | Nhóm **Kiểm soát cổng rạp**: `Soát vé QR`, `Lịch sử soát vé` + `Xem giao diện khách ↗` |
| **`MANAGER`** | Chỉ hiển thị liên kết khách hàng | **1 nút duy nhất:** `Console Vận hành →` (`/management/reports`) | **Kiểm soát cổng rạp** + **Quản trị & Lịch chiếu** (`Báo cáo`, `Quản lý Phim`, `Lịch chiếu`, `Phòng chiếu`) |
| **`ADMIN`** | Chỉ hiển thị liên kết khách hàng | **1 nút duy nhất:** `Console Vận hành →` (`/management/reports`) | Toàn bộ phân hệ `MANAGER` & `STAFF` kèm nhãn vai trò `ADMIN` |

---

## 4. Danh Mục Tệp Đã Tạo Mới & Cập Nhật

| Nhóm | Đường dẫn tệp | Thao tác | Mô tả chi tiết |
|---|---|:---:|---|
| **Web Fonts** | `src/BetaGiaiPhong.Web/wwwroot/fonts/*.woff2` (21 tệp) + [`OFL-LICENSE.txt`](../../src/BetaGiaiPhong.Web/wwwroot/fonts/OFL-LICENSE.txt) | Tạo mới | Bộ font cục bộ `Be Vietnam Pro` (400–800) & `JetBrains Mono` (500, 700) hỗ trợ đầy đủ tiếng Việt. |
| **Cinema CSS** | [`src/BetaGiaiPhong.Web/wwwroot/css/cinema/01-tokens.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/01-tokens.css) | Tạo mới | Hệ thống biến thiết kế điện ảnh Obsidian, typography, spacing, shadow và tương phản màu. |
| **Cinema CSS** | [`src/BetaGiaiPhong.Web/wwwroot/css/cinema/02-base.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/02-base.css) | Tạo mới | `@font-face`, reset, `:focus-visible`, skip link, container linh hoạt và `prefers-reduced-motion`. |
| **Cinema CSS** | [`src/BetaGiaiPhong.Web/wwwroot/css/cinema/03-shell-cinema.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/03-shell-cinema.css) | Tạo mới | Khung khách hàng: header chuyển trạng thái theo cuộn, ô tìm kiếm phim, ngăn kéo di động, footer. |
| **Cinema CSS** | [`src/BetaGiaiPhong.Web/wwwroot/css/cinema/03-shell-ops.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/03-shell-ops.css) | Tạo mới | Khung vận hành nội bộ: sidebar `248px`, topbar vai trò/ca trực, ngăn kéo di động nhân sự. |
| **Cinema CSS** | [`src/BetaGiaiPhong.Web/wwwroot/css/cinema/04-components.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/04-components.css) | Tạo mới | Thư viện component nguyên tử `.bgp-*` và giao diện các trang hệ thống không phụ thuộc Bootstrap. |
| **ES Module** | [`src/BetaGiaiPhong.Web/wwwroot/js/modules/shell-nav.js`](../../src/BetaGiaiPhong.Web/wwwroot/js/modules/shell-nav.js) | Tạo mới | Module JS thuần quản lý cuộn header (`requestAnimationFrame`), ô tìm kiếm, menu tài khoản và ngăn kéo mobile. |
| **Razor Shells** | [`src/BetaGiaiPhong.Web/Views/_ViewStart.cshtml`](../../src/BetaGiaiPhong.Web/Views/_ViewStart.cshtml) | Cập nhật | Tự động định tuyến Dual-Shell giữa `_CinemaLayout` và `_OperationsLayout`. |
| **Razor Shells** | [`src/BetaGiaiPhong.Web/Views/Shared/_CinemaLayout.cshtml`](../../src/BetaGiaiPhong.Web/Views/Shared/_CinemaLayout.cshtml) | Tạo mới | Layout điện ảnh hướng khách hàng kèm cờ chuyển tiếp `UseLegacyBootstrap`. |
| **Razor Shells** | [`src/BetaGiaiPhong.Web/Views/Shared/_OperationsLayout.cshtml`](../../src/BetaGiaiPhong.Web/Views/Shared/_OperationsLayout.cshtml) | Tạo mới | Layout bảng điều khiển tác nghiệp nội bộ cho `STAFF`, `MANAGER`, `ADMIN`. |
| **Static Views** | [`Views/Home/Privacy.cshtml`](../../src/BetaGiaiPhong.Web/Views/Home/Privacy.cshtml), [`Views/Account/AccessDenied.cshtml`](../../src/BetaGiaiPhong.Web/Views/Account/AccessDenied.cshtml), [`Views/Home/AccessDenied.cshtml`](../../src/BetaGiaiPhong.Web/Views/Home/AccessDenied.cshtml), [`Views/Shared/Error.cshtml`](../../src/BetaGiaiPhong.Web/Views/Shared/Error.cshtml) | Cập nhật | Nâng cấp 100% sang hệ component `.bgp-*` (`UseLegacyBootstrap = false`), bổ sung ghi nhận nguồn dữ liệu phim thật. |
| **Playwright E2E** | [`e2e/tests/09-dual-shell-navigation.spec.ts`](../../e2e/tests/09-dual-shell-navigation.spec.ts) | Tạo mới | Bộ kiểm thử E2E chuyên biệt cho `UI-P1` (cuộn header, tìm kiếm `/movies?q=...`, phân tách 5 vai trò, kiểm tra zero-Bootstrap, WOFF2 fonts và chống tràn ngang `320px`–`1440px`). |
| **Playwright E2E** | [`e2e/tests/04-responsive-and-a11y.spec.ts`](../../e2e/tests/04-responsive-and-a11y.spec.ts), [`e2e/tools/capture-ui.ts`](../../e2e/tools/capture-ui.ts) | Cập nhật | Đồng bộ bài kiểm thử điều hướng di động `8.5` sang Dual-Shell và bổ sung kịch bản chụp 10 ảnh nghiệm thu `ui-p1`. |

---

## 5. Kết Quả Kiểm Chứng Kỹ Thuật Toàn Diện

| Hạng mục kiểm chứng | Lệnh thực thi | Kết quả |
|---|---|:---:|
| **1. Kiểm tra tĩnh lược đồ CSDL** | `python scripts/static_schema_checks.py` | **PASSED** (`17 tables, 116 columns, 22 FKs`) |
| **2. Biên dịch toàn bộ Solution (`Release`)** | `dotnet build -c Release --nologo` | **PASSED** (`0 Warning(s), 0 Error(s)`) |
| **3. Kiểm thử Đơn vị & Tích hợp xUnit** | `dotnet test -c Release --no-build --nologo` (trên DB tạm tự hủy) | **PASSED** (`166/166 passed, 0 failed`) |
| **4. Kiểm tra kiểu TypeScript E2E** | `npx tsc --noEmit` (trong `e2e/`) | **PASSED** (`0 errors`) |
| **5. Kiểm thử Trình duyệt Thực Playwright (`01` → `09`)** | `npx playwright test` | **PASSED** (`26/26 passed, 0 failed`) |
| **6. Đối soát Toàn vẹn Nghiệp vụ (`Runner verify`)** | Thực thi tự động cuối chu kỳ E2E & Capture | **INTEGRITY PASSED** (`0 violation(s)`) |

---

## 6. Bộ Ảnh Nghiệm Thu Thực Tế (`docs/ui/screenshots/ui-p1/`)

Toàn bộ 10 ảnh dưới đây được kết xuất từ trình duyệt Chromium thực trên máy chủ Kestrel và đã được kiểm tra trực quan từng khung hình:

1. [`01-cinema-header-top-overlay-desktop.png`](screenshots/ui-p1/01-cinema-header-top-overlay-desktop.png) — Header khách hàng ở đỉnh trang chủ (`1440×900`), trạng thái nền phủ gradient trong suốt, font `Be Vietnam Pro` sắc nét.
2. [`02-cinema-header-scrolled-search-desktop.png`](screenshots/ui-p1/02-cinema-header-scrolled-search-desktop.png) — Header khách hàng khi cuộn xuống (`scrollY > 32px`) chuyển sang kính tối `#0A0A0C` kèm ô tìm kiếm mở rộng.
3. [`03-privacy-provenance-desktop.png`](screenshots/ui-p1/03-privacy-provenance-desktop.png) — Trang `Giới thiệu, Tuyên bố Học thuật & Xuất xứ Dữ liệu` (`/privacy`, zero-Bootstrap).
4. [`04-access-denied-desktop.png`](screenshots/ui-p1/04-access-denied-desktop.png) — Trang `403 Truy Cập Bị Từ Chối` (`/account/access-denied`, zero-Bootstrap).
5. [`05-error-404-desktop.png`](screenshots/ui-p1/05-error-404-desktop.png) — Trang trạng thái `404 Không Tìm Thấy Nội Dung` kèm `Correlation ID` hiển thị bằng `JetBrains Mono` (`/error?statusCode=404`, zero-Bootstrap).
6. [`06-cinema-mobile-drawer-customer.png`](screenshots/ui-p1/06-cinema-mobile-drawer-customer.png) — Ngăn kéo điều hướng di động (`390×844`) của vai trò `CUSTOMER` với các mục chạm `>= 44px`.
7. [`07-cinema-header-manager-context-switch.png`](screenshots/ui-p1/07-cinema-header-manager-context-switch.png) — Giao diện khách hàng khi đăng nhập `MANAGER`: không làm bẩn thanh nav bằng link nội bộ, chỉ hiển thị duy nhất pill chuyển ngữ cảnh `Console Vận hành →`.
8. [`08-ops-shell-manager-desktop.png`](screenshots/ui-p1/08-ops-shell-manager-desktop.png) — Khung `_OperationsLayout` (`1440×900`) của `MANAGER` với sidebar tác nghiệp `248px` và topbar vai trò.
9. [`09-ops-shell-staff-desktop.png`](screenshots/ui-p1/09-ops-shell-staff-desktop.png) — Khung `_OperationsLayout` (`1440×900`) của `STAFF` chỉ hiển thị nhóm `Kiểm soát cổng rạp` (ẩn hoàn toàn nhóm Quản trị).
10. [`10-ops-shell-staff-mobile-drawer.png`](screenshots/ui-p1/10-ops-shell-staff-mobile-drawer.png) — Ngăn kéo tác nghiệp di động (`390×844`) của `STAFF` với nút đóng `.bgp-icon-btn` đồng bộ nền tối.

---

## 7. Sẵn Sàng Chuyển Giao Sang Giai Đoạn Tiếp Theo

Nền tảng Design System và kiến trúc Dual-Shell của `UI-P1` đã hoàn chỉnh, ổn định và đạt 100% kiểm thử tự động. Hệ thống sẵn sàng bước sang:
- **Giai đoạn 2 (`BE/DATA-P2`):** Mở rộng mô hình dữ liệu & nạp gói danh mục phim thật đã xác minh bản quyền/nguồn mở (`REAL_MOVIE_CONTENT_PLAN.md`).
- **Giai đoạn 3 (`UI-P3`):** Tái thiết toàn diện Trang chủ Hero Billboard, Hàng phim, Danh mục phim & Chi tiết phim thuần túy trên hệ CSS mới (`UseLegacyBootstrap = false`).
