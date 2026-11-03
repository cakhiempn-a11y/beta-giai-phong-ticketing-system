# Báo Cáo Triển Khai Giai Đoạn 3 (`UI-P3`): Trải Nghiệm Khám Phá Điện Ảnh (Trang Chủ, Danh Mục, Chi Tiết Phim, Hover Preview & Trailer)

- **Dự án:** Hệ thống Đặt vé Rạp chiếu phim Beta Giải Phóng (*MIS Academic Capstone*)
- **Giai đoạn:** `UI-P3` — *Cinematic Discovery Experience (`/`, `/movies`, `/movies/{id}`)*
- **Tham chiếu kiến trúc:** [`docs/ui/NETFLIX_UI_MASTER_PLAN.md`](NETFLIX_UI_MASTER_PLAN.md) (Mục 11 — Giai đoạn 3 `UI-P3`)
- **Trạng thái:** **HOÀN TẤT & ĐÃ XÁC MINH TOÀN DIỆN (PASS 100%)**

---

## 1. Tóm Tắt Mục Tiêu Đã Đạt Được

Giai đoạn `UI-P3` chuyển đổi hoàn toàn 3 trang khám phá trọng tâm của khách hàng (`/`, `/movies`, `/movies/{id}`) sang hệ thống thiết kế Netflix-Cinema trên `_CinemaLayout.cshtml` với cờ **`ViewData["UseLegacyBootstrap"] = false`** (tách bỏ 100% `bootstrap.min.css`, `bootstrap.bundle.min.js`, `dark-compat.css` và `jQuery`):

1. **Trang chủ Điện ảnh (`/` — `Views/Home/Index.cshtml`):**
   - **Full-Bleed Photographic Hero Billboard:** Hiển thị ảnh ngang `16:9` xác minh độ phân giải cao (`srcset` `780w`/`1280w`, `fetchpriority="high"`, `loading="eager"`), lớp phủ gradient hai hướng tạo vùng chữ an toàn, nhãn `ĐANG CHIẾU`, điểm đánh giá thật kèm nguồn + thang điểm + số lượt (`IMDb 7.8/10 (95.223 lượt)`), nhãn phân loại tuổi tại Việt Nam (hoặc ghi rõ `"Chưa có phân loại tại VN"`), tên gốc + năm phát hành + quốc gia, đạo diễn & dàn diễn viên thật, chip suất chiếu gần nhất (`UTC+7`) và cụm nút hành động (`Xem lịch chiếu`, `Xem trailer`, `Khám phá phim`).
   - **Rail 1 — `16:9` Đang Mở Bán Vé Trực Tuyến (`#on-sale-today`):** Thẻ ngang `16:9` đi kèm chip khung giờ chiếu `UTC+7` trỏ thẳng sang `/showtimes/{id}/seats` (hỗ trợ đánh dấu `+1n` khi qua nửa đêm) và tự động chuyển sang bố cục *Spotlight Grid* không lặp phim giả tạo khi số phim mở bán ít (`<= 3` phim).
   - **Rail 2 — `2:3` Phim Đang Chiếu & Danh Mục Tuyển Chọn (`#now-showing`):** Hàng poster dọc `2:3` chuẩn rạp (`srcset` `342w`/`780w`, `sizes`, `loading="lazy"`), thẻ `<a>` đơn thuần chuẩn W3C (không lồng `<button>`/`<a>` bên trong), đi kèm nút điều hướng trái/phải hỗ trợ bàn phím và `prefers-reduced-motion: reduce`.
   - **Bảng Lịch Chiếu Sắp Diễn Ra (`UTC+7`):** Bề mặt tối nhiều lớp trên `.bgp-table` không phụ thuộc Bootstrap.

2. **Hover / Focus Quick-Preview Popover (`preview-popover.js`):**
   - Đọc dữ liệu từ **SSR JSON Island** (`#bgp-quick-preview-island`, truy vấn gộp một lượt trên Backend qua `GetMoviesQuickPreviewMapAsync` để triệt tiêu N+1 request) và có cơ chế fallback gọi `/api/movies/{id}/quick-preview` khi cần.
   - Kích hoạt sau `320ms` hover intent chỉ trên thiết bị con trỏ chuột tinh (`matchMedia('(hover: hover) and (pointer: fine)')`), tự động khóa biên ngang/dọc trong viewport (`left`, `right`, `top`, `bottom` không bao giờ tràn mép màn hình), tắt hoàn toàn bẫy hover trên màn hình cảm ứng di động và đóng sạch bằng `Escape`, nút đóng hoặc khi cuộn trang.

3. **Click-to-Load Official YouTube Trailer Modal (`<dialog>` — `trailer-dialog.js`):**
   - Sử dụng phần tử chuẩn `<dialog id="bgp-trailer-dialog">` với trình phát bảo vệ quyền riêng tư `https://www.youtube-nocookie.com/embed/{id}`.
   - `<iframe>` chỉ được khởi tạo khi người dùng chủ động nhấn nút `Xem trailer` (0 tải ngầm video khi mở trang) và được hủy sạch (`about:blank` + gỡ khỏi DOM) ngay khi đóng hộp thoại hoặc nhấn `Escape`.

4. **Trang Danh Mục Phim (`/movies` — `Views/Movies/Index.cshtml`):**
   - Thanh công cụ tìm kiếm (theo tên tiếng Việt, tên gốc `OriginalTitle`, đạo diễn `DirectorNames`, thể loại `GenreText`), bộ lọc trạng thái suất chiếu (`Tất cả`, `Có lịch chiếu`, `Chưa có lịch chiếu`) và chip gợi ý thể loại đồng bộ qua URL query (`?q=&filter=`), giữ nguyên trạng thái khi `F5` hoặc nhấn `Back`.
   - Lưới poster `2:3` đáp ứng từ `2` cột (`320px`/`390px`) đến `5–6` cột (`1440px+`), hiển thị trung thực placeholder trung tính `/images/poster-fallback.svg` cho các phim Việt Nam chờ khóa API TMDB (`PENDING_TMDB_API_KEY` như *Mai*, *Đào, phở và piano*).

5. **Trang Chi Tiết Phim (`/movies/{id}` — `Views/Movies/Details.cshtml`):**
   - Sân khấu backdrop `16:9` mờ phía sau kết hợp khung poster dọc `2:3` nguyên bản (không bao giờ kéo giãn ảnh dọc thành banner ngang), lưới diễn viên & vai diễn xác minh (`Cast`), nút mở trailer chính thức và neo cuộn mượt xuống `#showtimes-section` có bù trừ chiều cao thanh điều hướng cố định.
   - Không gian hai cột phía dưới tách bạch rõ ràng:
     - **Cột trái:** Bộ chọn ngày chiếu theo múi giờ Việt Nam (`UTC+7`, điều hướng phím mũi tên `ArrowLeft`/`ArrowRight`/`Home`/`End`, đồng bộ `?date=yyyy-MM-dd` và hoạt động đầy đủ kể cả khi tắt JavaScript), nhãn qua nửa đêm `+1 ngày (dd/MM)`, trạng thái hết ghế/sắp hết ghế, hoặc khối gợi ý các phim đang mở bán vé khi phim hiện tại chưa có lịch chiếu.
     - **Cột phải:** Khối **"Hồ sơ Nguồn Dữ liệu Tác phẩm"** đối chiếu minh bạch mã Wikidata (`QID`), mã IMDb, nguồn điểm & số lượt bình chọn, phân loại độ tuổi tại thị trường Việt Nam, nguồn/giấy phép hình ảnh và ghi chú tách biệt lịch chiếu học thuật MIS.

---

## 2. Danh Sách Tệp Tạo Mới & Cập Nhật

### Backend, DTO & ViewModels
- `src/BetaGiaiPhong.Web/Models/Responses/MovieDtos.cs`: Bổ sung `CrossesMidnight` vào `QuickPreviewShowtimeChipDto`.
- `src/BetaGiaiPhong.Web/Services/CatalogService.cs`: Triển khai `GetMoviesQuickPreviewMapAsync` truy vấn gộp theo lô cho SSR JSON Island.
- `src/BetaGiaiPhong.Web/Models/ViewModels/HomeViewModel.cs`, `MovieCatalogViewModel.cs`, `MovieDetailViewModel.cs`: Bổ sung dữ liệu dàn diễn viên Hero, danh sách phim mở bán `16:9`, chip thể loại, phim gợi ý khi chưa có suất chiếu và `QuickPreviewJson`.
- `src/BetaGiaiPhong.Web/Controllers/HomeController.cs`, `MoviesController.cs`: Chuẩn hóa lựa chọn phim Hero có backdrop thật + trailer + lịch chiếu và serialize JSON an toàn với `JavaScriptEncoder.Default`.
- `src/BetaGiaiPhong.Runner/DemoSeeder.cs`: Bổ sung suất chiếu đêm `23:15` (qua nửa đêm `+1 ngày`) cho *Mê trong mê* (`Q496266`) phục vụ kiểm chứng trực quan đa ngày `UTC+7`.

### Giao diện CSS & Vanilla ES Modules (Zero-Bootstrap)
- `src/BetaGiaiPhong.Web/wwwroot/css/cinema/04-components.css`: Hệ thống `.bgp-age-badge`, `.bgp-badge--imdb-solid`, `.bgp-time-pills`, `.bgp-preview-popover`, `.bgp-trailer-dialog`.
- `src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-home.css`: Bố cục trang chủ điện ảnh, Hero toàn chiều ngang, Rail `16:9` & `2:3`, bảng lịch chiếu nhanh.
- `src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-catalog.css`: Thanh lọc tìm kiếm và lưới poster `2:3` đa điểm gãy.
- `src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-detail.css`: Sân khấu chi tiết phim, lưới diễn viên, tab ngày `UTC+7` và khối hồ sơ nguồn dữ liệu.
- `src/BetaGiaiPhong.Web/wwwroot/js/modules/rail-carousel.js`: Điều khiển cuộn ngang hàng phim đa thực thể.
- `src/BetaGiaiPhong.Web/wwwroot/js/modules/preview-popover.js`: Bảng xem nhanh `320ms` tự khóa biên viewport.
- `src/BetaGiaiPhong.Web/wwwroot/js/modules/trailer-dialog.js`: Hộp thoại `<dialog>` nhúng `youtube-nocookie.com` click-to-load và tự dọn `<iframe>`.
- `src/BetaGiaiPhong.Web/wwwroot/js/modules/showtime-picker.js`: Bộ chọn ngày chiếu `UTC+7` tăng cường lũy tiến (Progressive Enhancement).
- `src/BetaGiaiPhong.Web/Views/Home/Index.cshtml`, `Views/Movies/Index.cshtml`, `Views/Movies/Details.cshtml`: Nâng cấp toàn diện sang thiết kế mới với `UseLegacyBootstrap = false`.

### Kiểm thử Tự động & Công cụ Chụp Giao diện
- `e2e/tests/04-responsive-and-a11y.spec.ts`: Cập nhật kiểm chứng độ tương phản WCAG AAA/AA trên `/` và `/movies` không Bootstrap.
- `e2e/tests/07-cinematic-home.spec.ts`: Mở rộng lên 10 kịch bản (`7.1`–`7.10`) bao phủ Zero-Bootstrap, `fetchpriority="high"`, không nhãn giả, khóa biên Hover Popover, chặn hover trên cảm ứng và dọn `<iframe>` Trailer `<dialog>`.
- `e2e/tests/08-movie-discovery.spec.ts`: Mở rộng lên 7 nhóm kịch bản lớn bao phủ `srcset`/`sizes`, dàn diễn viên thật, Hồ sơ Nguồn Dữ liệu Tác phẩm, Trailer `<dialog>` và gợi ý phim đang mở bán.
- `e2e/tests/10-verified-movie-content.spec.ts`: Đồng bộ kiểm chứng ảnh Hero `[data-testid="hero-backdrop-img"]`.
- `e2e/tools/capture-ui.ts`: Bổ sung bộ chụp `ui-p3` (13 ảnh trên dữ liệu 11 phim thật đã xác minh).

---

## 3. Bằng Chứng Kiểm Định Kỹ Thuật

| Hạng mục kiểm tra | Lệnh thực thi | Kết quả |
| :--- | :--- | :--- |
| Kiểm tra tĩnh Schema & C# Delimiters | `python scripts/static_schema_checks.py` | **PASS** (`17` bảng, `145` cột, `22` FK, `39` SQL batches) |
| Biên dịch Release toàn giải pháp | `dotnet build -c Release --nologo` | **PASS** (`0 Warning(s)`, `0 Error(s)`) |
| Kiểm tra kiểu tĩnh TypeScript E2E | `npx tsc --noEmit` | **PASS** (`0` lỗi) |
| Kiểm thử đơn vị & tích hợp xUnit | `dotnet test -c Release --no-build` | **PASS** (`171/171` bài đạt, `0` lỗi) |
| Kiểm thử trình duyệt thật Playwright | `npx playwright test` (`01` đến `10`) | **PASS** (`32/32` bài đạt trên Chromium) |
| Kiểm tra toàn vẹn dữ liệu vận hành | `BetaGiaiPhong.Runner verify` | **PASS** (`0 violation(s)`) |
| Kiểm định tràn ngang đa khung nhìn | `320px`, `390px`, `768px`, `992px`, `1440px` | **PASS** (`scrollWidth <= clientWidth` trên `/`, `/movies`, `/movies/{id}`) |

---

## 4. Bộ Ảnh Chụp Giao Diện Thực Tế (`docs/ui/screenshots/ui-p3/`)

Toàn bộ 13 ảnh chụp dưới đây được kết xuất trực tiếp từ Kestrel + SQL Server tạm thời chứa 11 bộ phim thật đã xác minh (không chứa phim giả lập):

1. `01-home-hero-and-rails-desktop-1440.png` — Trang chủ Desktop `1440×900` với Hero *Mê trong mê* (*Charade*, 1963), Rail `16:9` mở bán vé nhanh, Rail `2:3` poster và bảng lịch chiếu `UTC+7`.
2. `02-home-quick-preview-popover-desktop-1440.png` — Bảng Hover Quick-Preview nổi trên mặt phẳng `#18181F`, tự khóa biên trái an toàn, hiển thị điểm IMDb thật + chip giờ chiếu + nút mở trailer.
3. `03-catalog-full-desktop-1440.png` — Trang `/movies` Desktop `1440×900` hiển thị 11 bộ phim thật và thanh công cụ tìm kiếm/lọc.
4. `04-catalog-no-showtimes-honest-placeholder-1440.png` — Trang `/movies?filter=no-showtimes` thể hiện trung thực placeholder trung tính `"CHƯA CÓ HÌNH ẢNH"` cho *Đào, phở và piano* (`T13`) và *Mai* (`T18`).
5. `05-catalog-empty-filter-desktop-1440.png` — Trạng thái không khớp bộ lọc trên `/movies` kèm nút đặt lại rõ ràng.
6. `06-movie-detail-charade-desktop-1440.png` — Trang `/movies/{id}` của *Mê trong mê* với poster `2:3` nguyên vẹn, dàn diễn viên xác minh, tab ngày `UTC+7` (có nhãn `+1 ngày (05/11)`) và khối *Hồ sơ Nguồn Dữ liệu Tác phẩm*.
7. `07-movie-detail-trailer-dialog-1440.png` — Cửa sổ `<dialog>` phát trailer chính thức qua `youtube-nocookie.com`.
8. `08-movie-detail-mai-zero-showtimes-1440.png` — Trang chi tiết phim *Mai* (`T18 (VN)`, chưa có suất chiếu) hiển thị khối gợi ý các phim đang mở bán vé tại rạp.
9. `09-home-tablet-768.png` — Trang chủ trên Tablet `768×1024`.
10. `10-home-mobile-390.png` — Trang chủ trên Mobile `390×844` (ẩn nút mũi tên rail, điều hướng vuốt cảm ứng tự nhiên).
11. `11-catalog-mobile-390.png` — Trang `/movies` lưới 2 cột cân đối trên Mobile `390×844`.
12. `12-movie-detail-charade-mobile-390.png` — Trang chi tiết phim trên Mobile `390×844`.
13. `13-home-narrow-mobile-320.png` — Kiểm chứng không tràn ngang trên màn hình siêu hẹp `320×568`.

---

## 5. Sẵn Sàng Cho Giai Đoạn Tiếp Theo (`UI-P4`)

Hành trình khám phá phim (`/`, `/movies`, `/movies/{id}`) đã khép kín hoàn chỉnh và nối thông sang đường dẫn `/showtimes/{id}/seats`. Dự án sẵn sàng bước sang **Giai đoạn 4 (`UI-P4`: Không gian Khán phòng Sơ đồ Ghế & Thanh Giữ Ghế Thời gian thực)** mà không ảnh hưởng tới bất kỳ bất biến đặt giữ ghế hay thanh toán nào của hệ thống.
