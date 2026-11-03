# Báo Cáo Triển Khai Giai Đoạn 7 (`UI-P7`): Bàn Làm Việc Vận Hành & Quản Trị (`STAFF`, `MANAGER`, `ADMIN`) + Công Cụ Đồng Bộ Nguồn Xác Minh

- **Dự án:** Hệ thống Đặt vé Rạp chiếu phim Beta Giải Phóng (*MIS Academic Capstone*)
- **Giai đoạn:** `UI-P7` — *High-Density Dark Operations Console (`/staff/*`, `/management/*`) & Controlled External Movie Provenance Lookup / Sync*
- **Tham chiếu kiến trúc:** [`docs/ui/NETFLIX_UI_MASTER_PLAN.md`](NETFLIX_UI_MASTER_PLAN.md) (Mục 11 — Giai đoạn 7 `UI-P7`) & [`docs/ui/prototype/06-operations-desktop.html`](prototype/06-operations-desktop.html)
- **Trạng thái:** **HOÀN TẤT & ĐÃ XÁC MINH TOÀN DIỆN (PASS 100%) — 100% TOÀN BỘ 26 RAZOR VIEWS ĐẠT ZERO-BOOTSTRAP / ZERO-JQUERY**

---

## 1. Tóm Tắt Mục Tiêu Đã Đạt Được

Giai đoạn `UI-P7` hoàn tất việc chuyển đổi toàn bộ **12 màn hình vận hành nội bộ** thuộc `Views/StaffCheckIn/*` (2 views) và `Views/Management/*` (10 views) trên vỏ giao diện chuyên dụng `_OperationsLayout.cshtml` với **`ViewData["UseLegacyBootstrap"] = false`**. Kể từ cột mốc này, **100% toàn bộ 26 Razor Views** trên cả hai vỏ giao diện (`_CinemaLayout` & `_OperationsLayout`) hoạt động hoàn toàn không phụ thuộc `bootstrap.min.css`, `bootstrap.bundle.min.js`, `dark-compat.css`, `jQuery`, `window.alert()` hay `window.confirm()`:

1. **Bàn Soát Vé QR Tốc Độ Cao Tông Tối Tại Cổng Rạp (`/staff/check-in` & `/staff/check-in/history`):**
   - **Khung ngắm quang học & Tải ảnh QR chuẩn công nghiệp:** Tích hợp khung ngắm camera `Html5Qrcode` (`#reader`), cảnh báo lỗi quyền truy cập camera hiển thị trực tiếp nội tuyến (`#cameraErrorBanner` — tuyệt đối không dùng `window.alert`), công tắc âm báo phản hồi Web Audio (`#btnToggleSound` phát hợp âm thành công hoặc cảnh báo lỗi) và nút tải ảnh QR tùy biến (`label[for="qrFileInput"].bgp-qr-upload-btn` + `#qrFileInput`).
   - **Đường ống giải mã ảnh QR đa tầng tất định (`qr-scanner.js`):** Kết hợp `window.BarcodeDetector({ formats: ["qr_code"] })` có vùng đệm trắng (`native_pad48`, `native_2x_pad64`) với bộ biến đổi đa tỷ lệ `ZXing` trên canvas ẩn `#qrFileScratch` và `Html5Qrcode.scanFile`, giải mã chính xác 100% mọi ảnh chụp DOM `.bgp-qr-box` bất kể biến thiên bit ngẫu nhiên của nonce `AES-256-GCM`, đồng thời từ chối an toàn tệp hỏng/ảnh rỗng với mã `INVALID_QR_IMAGE`.
   - **Đầu đọc USB Barcode/2D Imager & Phản hồi trạng thái tương phản cao:** Ô nhập mã dự phòng `#manualQrToken` (`autocomplete="off"`) + `#btnManualSubmit`, tự động làm sạch ô nhập sau mỗi lượt quét, chống gửi đúp (`isSubmitting`), khóa cứng giờ chiếu & thời điểm quét theo **`UTC+7` (`Asia/Ho_Chi_Minh`)**, hiển thị bằng `textContent` chống XSS và **tuyệt đối không ghi log chuỗi mã QR thô ra `console`**.
   - **Nhật ký kiểm toán cổng rạp (`/staff/check-in/history`):** Bảng tra cứu mật độ cao lọc theo suất chiếu và kết quả quét (`ACCEPTED`, `ALREADY_USED`, `WRONG_SHOWTIME`, `CANCELLED_OR_REFUNDED`, `NOT_FOUND`, `INVALID_SIGNATURE`) kèm lý do nghiệp vụ rõ ràng.

2. **Bàn Làm Việc Quản Trị Mật Độ Cao Cho `MANAGER` / `ADMIN` (`/management/*`):**
   - **Báo cáo & Doanh thu (`/management/reports`):** Hệ thống thẻ KPI phân tách dải màu ngữ nghĩa (Tổng doanh thu **VND nguyên**, Tổng vé đã bán, Tỷ lệ lấp đầy trung bình, Hiệu suất soát vé cổng rạp), bộ lọc khoảng ngày `UTC+7` có nút chọn nhanh (`Hôm nay`, `7 ngày`, `30 ngày`) và hai bảng phân tích chi tiết theo từng Phim & từng Suất chiếu kèm thanh tiến độ công suất trực quan.
   - **Lịch chiếu, Phòng chiếu & Ma trận Sơ đồ Ghế (`/management/showtimes/*`, `/management/rooms/*`):** Màn hình `ShowtimeDetail` và `RoomDetail` kết xuất ma trận sơ đồ ghế phòng chiếu trên nền tối với màn hình cong điện ảnh (`AVAILABLE`, `HELD`, `SOLD`, `BLOCKED` hoặc hạng ghế vật lý `STANDARD` / `VIP` / `SWEETBOX`). Mọi hành động nhạy cảm (`Lưu trữ phim ARCHIVED`, `Đóng bán vé CLOSED`, `Hủy suất chiếu CANCELLED`) đều sử dụng hộp thoại xác nhận chuẩn **`<dialog>` HTML5** (`ops-UX.js`) thay thế hoàn toàn `window.confirm()`.

3. **Công Cụ Tra Cứu & Đồng Bộ Nguồn Phim Xác Minh Có Kiểm Soát (`/management/movies` & `/management/movies/{id}/edit`):**
   - **Tra cứu trực tiếp danh mục xác minh & TMDB (`GET /api/management/movies/external-lookup`):** Cho phép `MANAGER`/`ADMIN` tìm theo tên phim, mã `Wikidata QID` (`Q...`), `IMDb ID` (`tt...`) hoặc `TMDB ID`. Khi biến môi trường `BGP_TMDB_READ_TOKEN` chưa được cấu hình, giao diện hiển thị minh bạch huy hiệu **`Chế độ không khóa TMDB (Wikidata/IMDb sẵn sàng)`** và tra cứu ngay trên kho đối chiếu xác minh cục bộ (`verified-movies.catalog.json` mở rộng gồm 13 tác phẩm thật có đầy đủ nguồn `Wikidata` + `IMDb`); khi có khóa `BGP_TMDB_READ_TOKEN`, hệ thống kết hợp truy vấn trực tiếp API TMDB v3 mà không bao giờ để lộ khóa ra trình duyệt.
   - **Nhập phim ở trạng thái `DRAFT` & Không tự suy diễn nhãn độ tuổi VN (`POST /management/movies/sync-external`):** Tác phẩm nhập từ nguồn ngoài luôn khởi tạo ở trạng thái **`DRAFT`** để quản lý rạp rà soát trước khi mở bán; tuyệt đối không tự động dịch phân loại tuổi nước ngoài (`PG-13`, `R`, ...) sang nhãn kiểm duyệt Việt Nam (`P`, `K`, `T13`, `T16`, `T18`, `C`).
   - **Khóa bảo toàn biên tập thủ công (`ManualOverrideMask`):** Khi quản lý rạp chỉnh sửa các trường biên tập (`title`, `age_label`, `synopsis`, `genre_text`, `poster_url`, `duration_min`, `rating_value`) trên `MovieEdit`, hệ thống tự động bật cờ khóa tương ứng trong `ManualOverrideMask` (và cho phép bật/tắt tường minh từng trường). Khi chạy **`Đồng bộ lại từ nguồn xác minh`** hoặc **`Đồng bộ danh mục xác minh`**, các trường đã khóa được giữ nguyên 100% không bị ghi đè.

---

## 2. Danh Sách Tệp Tạo Mới & Cập Nhật

### Backend, Services, DTOs, ViewModels & Controllers
- [`src/BetaGiaiPhong.Data/Resources/verified-movies.catalog.json`](../../src/BetaGiaiPhong.Data/Resources/verified-movies.catalog.json): Bổ sung các tác phẩm mã nguồn mở xác minh của Blender Foundation (*Big Buck Bunny* `Q731114` / `tt1254207`, *Elephants Dream* `Q737719` / `tt0807840`) phục vụ quy trình tra cứu & nhập nháp xác minh không cần khóa mạng.
- [`src/BetaGiaiPhong.Data/Services/VerifiedMovieCatalogSyncService.cs`](../../src/BetaGiaiPhong.Data/Services/VerifiedMovieCatalogSyncService.cs): Bổ sung `SearchExternalCandidatesAsync` (kết hợp danh mục xác minh cục bộ `Wikidata`/`IMDb` và TMDB v3 API khi có `BGP_TMDB_READ_TOKEN`), `ImportOrSyncExternalMovieAsync` (khởi tạo `DRAFT`, tôn trọng tuyệt đối `ManualOverrideMask`, không tự suy diễn `AgeLabel` VN) và `IsTmdbConfigured`.
- [`src/BetaGiaiPhong.Data/Services/MovieManagementService.cs`](../../src/BetaGiaiPhong.Data/Services/MovieManagementService.cs) & [`ManagementQueryService.cs`](../../src/BetaGiaiPhong.Data/Services/ManagementQueryService.cs): Mở rộng truy vấn & cập nhật đầy đủ các cột nguồn gốc xác minh (`OriginalTitle`, `ReleaseYear`, `DirectorNames`, `CountryName`, `OriginalLanguage`, `WikidataQid`, `ImdbId`, `TmdbId`, `BackdropUrl`, `TrailerYoutubeId`, `ArtworkSource`, `RatingSource`, `RatingValue`, `ManualOverrideMask`), tự động gán cờ bảo toàn khi biên tập viên thay đổi dữ liệu và chuẩn hóa điều kiện `CanCancel` cho suất chiếu chưa bán vé.
- [`src/BetaGiaiPhong.Web/Controllers/ManagementController.cs`](../../src/BetaGiaiPhong.Web/Controllers/ManagementController.cs), [`ManagementMovieViewModels.cs`](../../src/BetaGiaiPhong.Web/Models/ViewModels/ManagementMovieViewModels.cs), [`Program.cs`](../../src/BetaGiaiPhong.Web/Program.cs): Đăng ký `HttpClient` + `IVerifiedMovieCatalogSyncService` và bổ sung 3 endpoint quản trị có bảo vệ RBAC (`MANAGER`, `ADMIN`) + Anti-Forgery CSRF (`GET /api/management/movies/external-lookup`, `POST /management/movies/sync-external`, `POST /management/movies/sync-verified-catalog`).

### Giao Diện CSS, Vanilla ES Modules & 12 Operational Razor Views (Zero-Bootstrap)
- [`src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-ops.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-ops.css) & [`03-shell-ops.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/03-shell-ops.css): Hệ thống kiểu dáng chuyên biệt cho Console Vận hành mật độ cao (thẻ KPI, bộ lọc tác vụ, bảng dữ liệu vận hành, huy hiệu nguồn xác minh `Wikidata`/`IMDb`/`TMDB`, ma trận ghế phòng chiếu, hộp thoại `<dialog>`, khung ngắm cổng soát vé QR và tương thích di động `320px`–`1440px`).
- [`src/BetaGiaiPhong.Web/wwwroot/js/modules/qr-scanner.js`](../../src/BetaGiaiPhong.Web/wwwroot/js/modules/qr-scanner.js): Module ES thuần điều khiển camera cổng rạp, âm báo Web Audio, giải mã ảnh QR đa tầng (`BarcodeDetector` + `ZXing` + `Html5Qrcode`), đầu đọc USB/thủ công và hiển thị kết quả an toàn theo múi giờ `UTC+7`.
- [`src/BetaGiaiPhong.Web/wwwroot/js/modules/ops-management.js`](../../src/BetaGiaiPhong.Web/wwwroot/js/modules/ops-management.js): Module ES thuần quản lý hộp thoại xác nhận `<dialog>`, nút chọn nhanh khoảng ngày báo cáo, kiểm tra hợp lệ biểu mẫu không cần `jQuery` và bảng tra cứu/nhập phim xác minh trực tiếp trên `/management/movies`.
- **12 Razor Views chuyển sang `ViewData["UseLegacyBootstrap"] = false`:**
  - `Views/StaffCheckIn/`: [`Index.cshtml`](../../src/BetaGiaiPhong.Web/Views/StaffCheckIn/Index.cshtml), [`History.cshtml`](../../src/BetaGiaiPhong.Web/Views/StaffCheckIn/History.cshtml).
  - `Views/Management/`: [`Reports.cshtml`](../../src/BetaGiaiPhong.Web/Views/Management/Reports.cshtml), [`Movies.cshtml`](../../src/BetaGiaiPhong.Web/Views/Management/Movies.cshtml), [`MovieCreate.cshtml`](../../src/BetaGiaiPhong.Web/Views/Management/MovieCreate.cshtml), [`MovieEdit.cshtml`](../../src/BetaGiaiPhong.Web/Views/Management/MovieEdit.cshtml), [`MovieDetail.cshtml`](../../src/BetaGiaiPhong.Web/Views/Management/MovieDetail.cshtml), [`Showtimes.cshtml`](../../src/BetaGiaiPhong.Web/Views/Management/Showtimes.cshtml), [`ShowtimeCreate.cshtml`](../../src/BetaGiaiPhong.Web/Views/Management/ShowtimeCreate.cshtml), [`ShowtimeDetail.cshtml`](../../src/BetaGiaiPhong.Web/Views/Management/ShowtimeDetail.cshtml), [`Rooms.cshtml`](../../src/BetaGiaiPhong.Web/Views/Management/Rooms.cshtml), [`RoomDetail.cshtml`](../../src/BetaGiaiPhong.Web/Views/Management/RoomDetail.cshtml).

### Kiểm Thử Tự Động (xUnit & Playwright) và Công Cụ Chụp Bằng Chứng
- [`tests/BetaGiaiPhong.Tests/WebTests/StaffCheckInWebTests.cs`](../../tests/BetaGiaiPhong.Tests/WebTests/StaffCheckInWebTests.cs) & [`ManagementWebTests.cs`](../../tests/BetaGiaiPhong.Tests/WebTests/ManagementWebTests.cs): Cập nhật `TC_WEB_STAFF_18` cho kiến trúc ES module và bổ sung `TC_MGMT_25` (kiểm định RBAC + phản hồi tra cứu nguồn xác minh không khóa TMDB), `TC_MGMT_26` (kiểm định vòng đời nhập `DRAFT` + bảo toàn khóa ghi đè `ManualOverrideMask` khi đồng bộ lại).
- [`e2e/tests/14-operations-console-and-tmdb-sync.spec.ts`](../../e2e/tests/14-operations-console-and-tmdb-sync.spec.ts), [`02-negative-and-security.spec.ts`](../../e2e/tests/02-negative-and-security.spec.ts), [`11-cinematic-seat-map.spec.ts`](../../e2e/tests/11-cinematic-seat-map.spec.ts): Bộ kiểm thử trình duyệt thật (`14.1`–`14.4`) xác minh cô lập Zero-Bootstrap trên toàn bộ các tuyến `/staff/*` và `/management/*`, quét ảnh QR thật (`ACCEPTED` -> `ALREADY_USED`), từ chối ảnh hỏng (`INVALID_QR_IMAGE`), kiểm toán không lộ mã QR trên `console`, tra cứu & nhập nháp phim thật (*Big Buck Bunny* `Q731114`), khóa `ManualOverrideMask` không bị ghi đè khi chạy đồng bộ, hộp thoại `<dialog>` lưu trữ/đóng/hủy suất chiếu và `0px` tràn ngang từ `320px` đến `1440px`.
- [`e2e/tools/capture-ui.ts`](../../e2e/tools/capture-ui.ts): Bổ sung chế độ chụp `ui-p7` xuất trọn bộ 11 ảnh chụp màn hình thực tế vào `docs/ui/screenshots/ui-p7/`.

---

## 3. Bằng Chứng Kiểm Định Kỹ Thuật

| Hạng mục kiểm tra | Lệnh thực thi | Kết quả |
| :--- | :--- | :--- |
| Kiểm tra tĩnh Schema & C# Delimiters | `python scripts/static_schema_checks.py` | **PASS** (`17` bảng, `145` cột, `22` FK, `39` SQL batches) |
| Biên dịch Release toàn giải pháp | `dotnet build -c Release --nologo` | **PASS** (`0 Warning(s)`, `0 Error(s)`) |
| Kiểm tra kiểu tĩnh TypeScript E2E | `npx tsc --noEmit` | **PASS** (`0` lỗi) |
| Kiểm thử đơn vị, tích hợp & bảo mật xUnit | `dotnet test -c Release --no-build --nologo` | **PASS** (`173/173` bài đạt) |
| Kiểm thử trình duyệt thật Playwright | `npx playwright test --reporter=list` (`01` đến `14`) | **PASS** (`48/48` bài đạt trên Chromium) |
| Cô lập Zero-Bootstrap trên toàn ứng dụng | Kiểm tra DOM `link[rel="stylesheet"]` & `script[src]` trên 26/26 Views | **PASS** (`0` trang còn nạp `bootstrap`, `dark-compat` hay `jquery`) |
| Kiểm tra toàn vẹn dữ liệu vận hành | `BetaGiaiPhong.Runner verify` | **PASS** (`0 violation(s)`) |
| Kiểm định tràn ngang đa khung nhìn | `320px`, `390px`, `768px`, `1440px` trên `/staff/*` & `/management/*` | **PASS** (`0px` horizontal overflow) |

---

## 4. Bộ Ảnh Chụp Giao Diện Thực Tế (`docs/ui/screenshots/ui-p7/`)

Toàn bộ 11 ảnh chụp dưới đây được kết xuất trực tiếp từ trình duyệt Chromium chạy trên Kestrel + SQL Server tạm thời với dữ liệu tác phẩm xác minh thật:

1. `01-staff-checkin-ready-desktop-1440.png` — Cổng soát vé QR `/staff/check-in` Desktop `1440×900` ở trạng thái sẵn sàng trực cổng với khung ngắm camera, công tắc âm báo, nút tải ảnh QR và đầu đọc USB dự phòng.
2. `02-staff-checkin-accepted-desktop-1440.png` — Phản hồi **`HỢP LỆ (ACCEPTED — MỜI VÀO RẠP)`** tương phản cao trên `/staff/check-in` sau khi giải mã ảnh QR thật của suất chiếu *Mê trong mê* (*Charade*, 1963), hiển thị đầy đủ ghế `C5`, phòng chiếu và dấu thời gian `UTC+7`.
3. `03-staff-checkin-history-desktop-1440.png` — Bảng nhật ký kiểm toán cổng soát vé `/staff/check-in/history` ghi nhận minh bạch các lượt quét `ACCEPTED`, `ALREADY_USED` và `NOT_FOUND`.
4. `04-staff-checkin-mobile-390.png` — Cổng soát vé QR `/staff/check-in` trên thiết bị cầm tay di động `390×844` dành cho nhân viên đứng cổng, tối ưu thao tác một tay và `0px` tràn ngang.
5. `05-mgmt-reports-dashboard-desktop-1440.png` — Bảng điều khiển Báo cáo & Doanh thu `/management/reports` Desktop `1440×900` với 4 thẻ KPI dải màu, bộ lọc ngày `UTC+7` và bảng hiệu suất theo từng Phim & Suất chiếu.
6. `06-mgmt-movies-external-lookup-desktop-1440.png` — Trang Quản lý Danh mục Phim `/management/movies` Desktop `1440×900` mở bảng **Tra cứu & Nhập nguồn xác minh (`Wikidata` / `IMDb` / `TMDB`)**, huy hiệu trạng thái không khóa TMDB và bảng danh mục phim hiển thị trọn vẹn 8 cột không bị cắt ngang.
7. `07-mgmt-movie-edit-override-locks-desktop-1440.png` — Màn hình Chỉnh sửa Phim `/management/movies/{id}/edit` của tác phẩm nhập nháp *Elephants Dream* (`Q737719` / `tt0807840`), hiển thị phân loại tuổi VN `T13`, hồ sơ định danh xác minh và bảng **Khóa bảo toàn biên tập thủ công (`ManualOverrideMask`)**.
8. `08-mgmt-movie-detail-provenance-desktop-1440.png` — Hồ sơ Chi tiết Phim Quản trị `/management/movies/{id}` hiển thị huy hiệu nguồn xác minh (`Wikidata: Q737719`, `IMDb: tt0807840`, `TMDB: #39079`, `CC BY 2.5`), nhãn khóa bảo toàn `age_label,title` và các tác vụ vòng đời (`Đồng bộ lại từ nguồn xác minh`, `Xuất bản phim`).
9. `09-mgmt-showtime-detail-matrix-desktop-1440.png` — Chi tiết Suất chiếu `/management/showtimes/{id}` với 4 thẻ chỉ số thời gian thực, bảng giá theo hạng ghế và ma trận sơ đồ phòng chiếu hiển thị đồng thời ghế `AVAILABLE`, `HELD` (`A4`) và `SOLD` (`C5`, `C6`).
10. `10-mgmt-room-detail-matrix-desktop-1440.png` — Chi tiết Phòng chiếu `/management/rooms/{id}` kết xuất sơ đồ bố trí ghế vật lý tham chiếu (`STANDARD` hàng `A–B`, `VIP` hàng `C–D`) dưới màn hình cong điện ảnh.
11. `11-mgmt-movies-mobile-390.png` — Trang Quản lý Danh mục Phim `/management/movies` trên khung nhìn di động `390×844` với thanh công cụ thích ứng và bảng cuộn nội bộ mượt mà không gây tràn ngang trang.
