# Báo Cáo Triển Khai Giai Đoạn 4 (`UI-P4`): Sơ Đồ Ghế Điện Ảnh Đa Dấu Hiệu & Tương Tác Bàn Phím Toàn Diện

- **Dự án:** Hệ thống Đặt vé Rạp chiếu phim Beta Giải Phóng (*MIS Academic Capstone*)
- **Giai đoạn:** `UI-P4` — *Multi-Cue Cinematic Auditorium Seat Map & Full 2D Keyboard Accessibility (`/showtimes/{id}/seats`)*
- **Tham chiếu kiến trúc:** [`docs/ui/NETFLIX_UI_MASTER_PLAN.md`](NETFLIX_UI_MASTER_PLAN.md) (Mục 11 — Giai đoạn 4 `UI-P4`)
- **Trạng thái:** **HOÀN TẤT & ĐÃ XÁC MINH TOÀN DIỆN (PASS 100%)**

---

## 1. Tóm Tắt Mục Tiêu Đã Đạt Được

Giai đoạn `UI-P4` chuyển đổi hoàn toàn trang chọn ghế `/showtimes/{id}/seats` (`Views/Showtimes/Seats.cshtml`) sang kiến trúc `_CinemaLayout.cshtml` với cờ **`ViewData["UseLegacyBootstrap"] = false`** (tách bỏ 100% `bootstrap.min.css`, `bootstrap.bundle.min.js`, `dark-compat.css`, `jQuery` và hộp thoại chặn luồng `window.alert()`):

1. **Sân Khấu Khán Phòng Tối Nhiều Lớp & Vòm Màn Chiếu Phát Sáng:**
   - Nền rạp tối sâu (`#0A0A0C` → `#101015`), thanh tiến trình đặt vé 4 bước rõ ràng, vòm màn hình chiếu cong có hào quang ánh sáng nhẹ hướng xuống ma trận ghế và bố cục cánh khán phòng 3 dải có lối đi tự nhiên cho phòng chiếu từ `8` cột trở lên.

2. **Hệ Thống Ghế 5 Trạng Thái Đa Dấu Hiệu (Multi-Cue Accessibility — Đọc Rõ Kể Cả Ở Thang Xám 100%):**
   - Mỗi ô ghế là phần tử tương tác chuẩn `<button type="button">` kết hợp đồng thời **hình dáng viền + độ sáng/hoa văn nền + ký hiệu trực quan** để không phụ thuộc duy nhất vào màu sắc:
     - **Ghế Thường còn trống (`•`):** Viền xám xanh chắc chắn, thanh đệm đáy mảnh, nền `#1E1E28`.
     - **Ghế VIP còn trống (`★`):** Thanh vương miện vàng ở đỉnh ghế (`::before`), viền vàng hổ phách và ngôi sao `★`.
     - **Ghế Đang chọn (`✓`):** Đỏ thương hiệu `#E50914`, viền trắng kép kèm hào quang sáng nổi bật và dấu kiểm `✓`.
     - **Ghế Đang tạm giữ (`⏳`):** Nền sọc chéo 45°, viền nét đứt hổ phách, ký hiệu đồng hồ cát `⏳` và `aria-disabled="true"`.
     - **Ghế Đã bán (`✕`) / Khóa kỹ thuật (`⊘`):** Nền chìm tối độ tương phản thấp, chữ gạch ngang, ký hiệu `✕`/`⊘` và `aria-disabled="true"`.

3. **Điều Hướng Ma Trận Bàn Phím 2 Chiều Chuẩn WAI-ARIA (*2D Roving Tabindex*):**
   - Chỉ duy nhất 1 điểm dừng `Tab` (`tabindex="0"`) để đi vào hoặc rời khỏi ma trận ghế (không bắt người dùng nhấn `Tab` qua hàng chục ghế).
   - Hỗ trợ đầy đủ `ArrowUp`, `ArrowDown`, `ArrowLeft`, `ArrowRight` (tự động ghép cột `data-col` gần nhất giữa các hàng), `Home` (đầu hàng), `End` (cuối hàng) và `Space` / `Enter` để chọn hoặc bỏ chọn ghế với vòng sáng `:focus-visible` trắng kép độ tương phản cao.
   - Các ghế `SOLD`/`HELD`/`BLOCKED` dùng `aria-disabled="true"` giúp người dùng bàn phím và trình đọc màn hình lướt xuyên mạch qua toàn bộ hàng/cột mà không bị kẹt thành ốc đảo, đồng thời nghe rõ lý do ghế không khả dụng.

4. **Cảnh Báo Giới Hạn 8 Ghế Nội Tuyến (`aria-live="polite"`) — Triệt Tiêu 100% `window.alert()`:**
   - Khi khách nhấn thêm ghế thứ 9, hệ thống giữ nguyên 8 ghế đang chọn và hiển thị tức thì dải thông báo cảnh báo nội tuyến `[data-testid="seat-limit-banner"]` nêu rõ mã ghế vừa chạm (ví dụ: `(không thể thêm ghế D7)`), kèm hiệu ứng rung phản hồi nhẹ tôn trọng `prefers-reduced-motion: reduce`.

5. **Cột Tóm Tắt Dính (*Sticky Summary Panel*) Với Dữ Liệu Tác Phẩm Xác Minh & Trung Thực Tuyệt Đối Về Giữ Chỗ:**
   - Hiển thị poster dọc `2:3` xác minh (`srcset` `342w`/`780w`), tên tiếng Việt, tên gốc + năm phát hành, điểm đánh giá thật (`IMDb`), nhãn độ tuổi tại thị trường Việt Nam, giờ bắt đầu và giờ kết thúc dự kiến theo múi giờ Việt Nam (`UTC+7`, tự động gắn nhãn `+1 ngày` khi suất chiếu vắt qua nửa đêm).
   - Danh sách chip ghế đang chọn cho phép gỡ nhanh từng ghế bằng nút `×`, tính tạm tính **VND nguyên** chính xác.
   - **Không hiển thị đồng hồ đếm ngược giả ở bước chọn ghế:** Khối `[data-testid="seat-hold-honesty-note"]` giải thích rõ ghế chỉ bắt đầu được khóa giao dịch và tính giờ giữ chỗ sau khi máy chủ xác nhận ở bước tiếp theo.

6. **Bảo Toàn Ghế Chọn Qua Luồng Đăng Nhập, Khóa Vai Trò Vận Hành & Tối Ưu Di Động `320px`–`390px`:**
   - **Khách chưa đăng nhập (`Guest`):** Lưu tạm danh sách ghế vào `sessionStorage` một lần (`bgp_login_draft_seats_{showtimeId}`, TTL 10 phút) khi bấm đăng nhập để đặt vé và tự động khôi phục các ghế còn trống ngay khi quay lại dưới vai trò `CUSTOMER`.
   - **Tài khoản nội bộ (`STAFF` / `MANAGER` / `ADMIN`):** Khóa thao tác chọn ghế (chế độ chỉ xem tra cứu sơ đồ khán phòng) kèm liên kết chuyển nhanh sang *Console Vận hành*.
   - **Suất chiếu đã đóng đặt vé trực tuyến:** Hiển thị thông báo trạng thái rõ ràng và nút quay lại chọn suất chiếu khác của cùng bộ phim.
   - **Di động (`320px`, `390px`):** Khung cuộn ngang cục bộ có bóng mờ hai biên (`has-scroll-left` / `has-scroll-right`), nút chuyển đổi **`Thu nhỏ vừa màn hình`** (`[data-testid="seat-zoom-toggle"]`) thu gọn trọn vẹn 8 cột ghế vừa khít khung nhìn `320px`/`390px` và thanh tóm tắt đáy cố định (*Bottom Sticky Dock*), đảm bảo `documentElement.scrollWidth <= clientWidth` trên mọi kích thước màn hình.

---

## 2. Danh Sách Tệp Tạo Mới & Cập Nhật

### Backend, DTO & ViewModels
- `src/BetaGiaiPhong.Web/Models/Responses/SeatMapDtos.cs`: Bổ sung các trường thông tin tác phẩm & lịch chiếu xác minh có giá trị mặc định tương thích ngược (`EndUtc`, `EndVietnamTimeOnlyText`, `CrossesMidnight`, `ShowtimeStatus`, `MovieDurationMin`, `MovieGenreText`, `MovieAgeLabel`, `MovieAgeLabelMarket`, `MovieOriginalTitle`, `MovieReleaseYear`, `MovieRating`, `MovieArtwork`).
- `src/BetaGiaiPhong.Web/Services/CatalogService.cs`: Làm giàu dữ liệu `GetShowtimeSeatMapAsync` trực tiếp từ thực thể `showtime.Movie` đã nạp sẵn (0 truy vấn SQL phát sinh thêm).
- `src/BetaGiaiPhong.Web/Models/ViewModels/SeatMapViewModel.cs`, `Controllers/ShowtimesController.cs`: Bổ sung `MaxColumnNo` phục vụ chia lối đi khán phòng.

### Giao Diện CSS, Vanilla ES Module & Razor View (Zero-Bootstrap)
- `src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-seat-map.css`: Hệ thống kiểu dáng khán phòng tối nhiều lớp, 5 trạng thái ghế đa dấu hiệu, vòm màn hình chiếu, cột tóm tắt dính, thanh đáy di động và chế độ thu nhỏ vừa màn hình `320px`/`390px`.
- `src/BetaGiaiPhong.Web/wwwroot/js/modules/seat-map.js`: Module ES độc lập xử lý *2D Roving Tabindex*, giới hạn 8 ghế nội tuyến, đồng bộ chip ghế & VND nguyên, bóng cuộn cục bộ, thu phóng vừa màn hình và khôi phục ghế nháp một lần qua `sessionStorage`.
- `src/BetaGiaiPhong.Web/Views/Showtimes/Seats.cshtml`: Nâng cấp toàn diện giao diện với `UseLegacyBootstrap = false`, đồng thời bảo toàn đầy đủ các định danh và hợp đồng biểu mẫu POST `/orders/reserve` (`#formReserve`, `#seatIdsCsv`, `#btnSubmit`, `.seat-grid-container`, `.seat-box`, `#selectedCount`, `#selectedTotal`, `#selectedSeatsBadges`, `#statAvailable`, `.alert-danger`).

### Kiểm Thử Tự Động & Công Cụ Chụp Giao Diện
- `e2e/tests/11-cinematic-seat-map.spec.ts`: Bộ kiểm thử E2E chuyên sâu cho `UI-P4` (`11.1`–`11.4`) kiểm chứng cô lập Zero-Bootstrap, độ phân biệt độ sáng ở chế độ `grayscale(1)`, đặt giữ ghế 100% bằng bàn phím, chặn 100% `window.alert()` khi chạm ghế thứ 9, khôi phục ghế qua luồng đăng nhập `returnUrl`, khóa tài khoản `STAFF`, suất chiếu đóng và không tràn ngang từ `320px` đến `1440px`.
- `e2e/tools/capture-ui.ts`: Bổ sung chế độ chụp `ui-p4` xuất 9 ảnh giao diện thực tế vào `docs/ui/screenshots/ui-p4/`.

---

## 3. Bằng Chứng Kiểm Định Kỹ Thuật

| Hạng mục kiểm tra | Lệnh thực thi | Kết quả |
| :--- | :--- | :--- |
| Kiểm tra tĩnh Schema & C# Delimiters | `python scripts/static_schema_checks.py` | **PASS** (`17` bảng, `145` cột, `22` FK, `39` SQL batches) |
| Biên dịch Release toàn giải pháp | `dotnet build -c Release --nologo` | **PASS** (`0 Warning(s)`, `0 Error(s)`) |
| Kiểm tra kiểu tĩnh TypeScript E2E | `npx tsc --noEmit` | **PASS** (`0` lỗi) |
| Kiểm thử đơn vị & tích hợp xUnit | `dotnet test -c Release --no-build --nologo` | **PASS** (`171/171` bài đạt, `0` lỗi) |
| Kiểm thử trình duyệt thật Playwright | `npx playwright test --reporter=list` (`01` đến `11`) | **PASS** (`36/36` bài đạt trên Chromium) |
| Kiểm tra toàn vẹn dữ liệu vận hành | `BetaGiaiPhong.Runner verify` | **PASS** (`0 violation(s)`) |
| Kiểm định tràn ngang đa khung nhìn | `320px`, `390px`, `768px`, `1440px` | **PASS** (`scrollWidth <= clientWidth` ở cả chế độ thường & thu nhỏ vừa màn hình) |

---

## 4. Bộ Ảnh Chụp Giao Diện Thực Tế (`docs/ui/screenshots/ui-p4/`)

Toàn bộ 9 ảnh chụp dưới đây được kết xuất trực tiếp từ Kestrel + SQL Server tạm thời trên suất chiếu phim thật *Mê trong mê* (*Charade*, 1963):

1. `01-seat-map-desktop-1440-selected-std-vip.png` — Khán phòng Desktop `1440×900` hiển thị đầy đủ 5 trạng thái ghế (`A3` Thường đang chọn, `C5`–`C6` VIP đang chọn, `C4` Đang tạm giữ sọc chéo, `B4` Đã bán), poster `2:3` xác minh và tạm tính `265.000 đ`.
2. `02-seat-map-desktop-1440-keyboard-focus-ring.png` — Vòng sáng tiêu điểm bàn phím `:focus-visible` trắng kép rõ nét trên ma trận *2D Roving Tabindex* (`C7`).
3. `03-seat-map-desktop-1440-grayscale-a11y.png` — Kiểm chứng khả năng phân biệt rành mạch cả 5 trạng thái ghế bằng hình dáng viền, hoa văn sọc chéo và ký hiệu (`•`, `★`, `✓`, `⏳`, `✕`) dưới bộ lọc thang xám `grayscale(1)`.
4. `04-seat-map-desktop-1440-max-8-inline-banner.png` — Cảnh báo nội tuyến `aria-live="polite"` khi chạm ghế thứ 9 (`không thể thêm ghế D7`), thay thế hoàn toàn `window.alert()`.
5. `05-seat-map-desktop-1440-closed-showtime.png` — Trạng thái suất chiếu đã đóng đặt vé trực tuyến kèm thông báo rõ ràng và nút chọn suất chiếu khác.
6. `06-seat-map-desktop-1440-operational-lock-staff.png` — Chế độ tra cứu khóa đặt vé dành cho tài khoản vận hành (`STAFF`) kèm lối tắt sang *Console Vận hành*.
7. `07-seat-map-mobile-390-scroll-and-dock.png` — Giao diện Mobile `390×844` với bóng mờ cuộn ngang cục bộ và thanh tóm tắt đáy cố định (*Bottom Sticky Dock*).
8. `08-seat-map-mobile-390-fit-zoom.png` — Giao diện Mobile `390×844` ở chế độ **`Thu nhỏ vừa màn hình`** hiển thị trọn vẹn 8 cột ghế (`A1..A8`) trong một khung nhìn.
9. `09-seat-map-narrow-mobile-320-fit-zoom.png` — Kiểm chứng màn hình siêu hẹp `320×568` ở chế độ thu nhỏ vừa màn hình: toàn bộ 8 cột ghế hiển thị rõ nét, không tràn ngang trang (`0px` overflow).
