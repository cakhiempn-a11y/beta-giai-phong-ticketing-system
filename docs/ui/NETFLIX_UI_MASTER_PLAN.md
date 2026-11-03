# KẾ HOẠCH TỔNG THỂ THIẾT KẾ LẠI GIAO DIỆN CHUẨN ĐIỆN ẢNH NETFLIX & DỮ LIỆU PHIM THẬT (`NETFLIX_UI_MASTER_PLAN`)

- **Dự án:** Hệ thống Bán vé Rạp chiếu phim Beta Giải Phóng (`https://github.com/cakhiempn-a11y/beta-giai-phong-ticketing-system`)
- **Tài liệu liên kết đồng bộ:**
  - Quyết định kiến trúc Frontend: [`FRONTEND_ARCHITECTURE_DECISION.md`](file:///d:/Projects/BetaGiaiPhong/docs/ui/FRONTEND_ARCHITECTURE_DECISION.md)
  - Kế hoạch dữ liệu phim thật & Media Pipeline: [`REAL_MOVIE_CONTENT_PLAN.md`](file:///d:/Projects/BetaGiaiPhong/docs/ui/REAL_MOVIE_CONTENT_PLAN.md)
  - Bộ Prototype tương tác & Ảnh chụp kết xuất: [`docs/ui/prototype/`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/README.md)

---

## Phần 1. Audit Hiện trạng Dự án (`Baseline: 4c0a3830`)

### 1.1. Trạng thái Git, PR, CI & Tiến độ Đã Hoàn thành
- **Nhánh & HEAD:** `main` tại commit `4c0a38301bfae3b99f3bf7f16c88e50e0b920453` (Merge PR #12 — `UI-02B`), cây thư mục sạch hoàn toàn (`git status` không có thay đổi dở dang). Tập tin `AGENTS.md` ở gốc dự án chưa tồn tại (`[Đã xác minh trực tiếp]`).
- **Trạng thái GitHub Actions mới nhất:** Run `41570604111` trên `main` đạt **SUCCESS** ở cả 4 job (`Build & Static Validation`, `Automated Tests with SQL Server` — 166 passed, `Integrity Checks & Runner Validation`, `Real-Browser E2E Gate` — 16 passed) (`[Đã xác minh trực tiếp]`).
- **Các giai đoạn UI trước đó đã triển khai:** `UI-01` & `UI-01A` (nền tối tương thích Bootstrap), `UI-02A` & `UI-02A-FIX` (trang chủ bước đầu), `UI-02B` (danh mục `/movies` và chi tiết phim `/movies/{id}` với bộ lọc URL + gom nhóm suất chiếu `UTC+7`).
- **Phạm vi kiểm chứng bằng trình duyệt thực tế:** Đã khởi chạy ứng dụng trên cơ sở dữ liệu tạm cô lập (`BetaGiaiPhong_E2E_audit_*`, tự động kiểm tra toàn vẹn `Runner verify` và xóa sạch sau khi chụp) để kiểm tra trực tiếp bằng Chromium toàn bộ 7 luồng: Trang chủ (`/`), Danh mục (`/movies`), Chi tiết phim (`/movies/{id}`), Chọn ghế (`/showtimes/1/seats`), Đơn hàng & Thanh toán (`/orders/{id}`, `/orders/{id}/checkout`), Vé QR (`/orders/{id}/tickets`), Nhân viên soát vé (`/staff/check-in`, `/staff/check-in/history`) và Quản trị (`/management/movies`, `create`, `showtimes`, `reports`).
  - *Mục duy nhất chưa xác minh phần cứng vật lý trực tiếp (`[Chưa xác minh trực tiếp phần cứng]`):* Luồng đọc hình ảnh trực tiếp từ ống kính webcam thật trên `/staff/check-in` do môi trường chạy Chromium không gắn camera vật lý (tuy nhiên cơ chế tải ảnh QR tĩnh, nhập mã thủ công và logic JS giải mã trong [`Index.cshtml`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Views/StaffCheckIn/Index.cshtml) đã được đọc mã nguồn và kiểm chứng giao diện).

### 1.2. Các Điểm Tốt Cần Kế thừa & Bảo toàn
1. **Hạt nhân nghiệp vụ và toàn vẹn dữ liệu rất vững (`[Đã xác minh trực tiếp + Đã đọc mã nguồn]`):** Khóa giao dịch chống bán trùng ghế, tính tiền nguyên VND không sai số dấu phẩy động, kiểm tra hạn giữ chỗ từ server (`HoldExpiresUtc`), chữ ký HMAC-SHA256 cho cổng thanh toán mô phỏng và mã hóa AES-GCM cho payload QR đều hoạt động chuẩn xác.
2. **Xử lý múi giờ Việt Nam (`UTC+7`) và Progressive Enhancement trên `/movies/{id}` (`[Đã đọc mã nguồn + Đã xác minh trực tiếp]`):** [`MoviesController.cs`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Controllers/MoviesController.cs) đã chuyển đổi `StartUtc`/`EndUtc` qua `DateTimeOffset` (`+07:00`), gắn nhãn `+1 ngày` cho suất qua nửa đêm và đồng bộ tham số `?date=yyyy-MM-dd`.
3. **Cơ chế Snapshot Fixture trong E2E (`[Đã đọc mã nguồn]`):** [`Program.cs`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Runner/Program.cs) của `BetaGiaiPhong.Runner` đã khôi phục dữ liệu chính xác theo `movie_id` và kiểm tra mã thoát nghiêm ngặt.

### 1.3. Khoảng cách Thị giác so với Netflix & Các Vấn đề Ưu tiên Cao (P0 / P1 / P2)

| Mức độ | Phát hiện cụ thể | Bằng chứng kiểm chứng | Tác động UX / Sản phẩm |
| :--- | :--- | :--- | :--- |
| **P0 (Bắt buộc xử lý)** | **0% nội dung phim thật & poster SVG giả lập:** Bảng `dbo.Movies` chỉ có 8 cột cơ bản; toàn bộ phim hiển thị là tên giả không dấu/mô phỏng (`Chien Binh Khong Gian...`, `Phim mo phong - KHONG phai lich Beta`) kèm ảnh vẽ hình học (`poster-scifi.svg`, `hero-cinema-backdrop.svg`). | `[Đã xác minh trực tiếp bằng trình duyệt]` trên `/`, `/movies`, `/movies/{id}` và `[Đã đọc mã nguồn]` [`Movie.cs`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Domain/Entities/Movie.cs). | Phá vỡ hoàn toàn cảm giác điện ảnh và vi phạm yêu cầu cốt lõi về nội dung tác phẩm thật có nguồn xác minh. |
| **P0 (Truy cập bàn phím & A11y)** | **Sơ đồ ghế không thể thao tác bằng bàn phím & Tooltip tối đen không đọc được:** Mỗi ghế trong [`Seats.cshtml`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Views/Showtimes/Seats.cshtml#L105-L116) là thẻ `<div class="seat-box">` không có `tabindex`, `role="button"` hay `aria-pressed`; phân biệt ghế Thường (xanh đậm) và VIP (tím đậm) gần như chỉ dựa vào sắc độ màu; tooltip Bootstrap hiển thị chữ xám tối trên nền đen mờ; chọn quá 8 ghế gọi `window.alert()`. | `[Đã xác minh trực tiếp bằng trình duyệt]` + `[Đã đọc mã nguồn]` `Seats.cshtml`. | Người dùng bàn phím hoàn toàn không đặt được vé; người khiếm sắc giác khó phân biệt ghế Thường/VIP và ghế Đã bán/Đang giữ. |
| **P1 (Thị giác & Hệ thống CSS)** | **Nợ kỹ thuật Bootstrap + `dark-compat.css` (`!important`):** 18/23 view vẫn gắn `.bg-white`, `.bg-light`, `.bg-primary`, `.text-primary`, `.table-light` khiến nhãn phòng chiếu, tổng tiền (`160.000 đ`), đầu thẻ vé điện tử và nút phụ bị nhuộm xanh dương `#0d6efd` chói gắt trên nền tối `#141414`; lạm dụng emoji hệ thống (`📋`, `🎟️`, `💳`, `⚠️`, `🎬`). | `[Đã xác minh trực tiếp bằng trình duyệt]` trên `Seats`, `Orders/*`, `Checkout`, `Tickets`, `Login`, `StaffCheckIn`, `Management/*`. | Giao diện mang cảm giác “trang quản trị Bootstrap sơn đen” thay vì sản phẩm điện ảnh có chiều sâu kiểu Netflix. |
| **P1 (Điều hướng & Khung trang)** | **Header phẳng, thiếu tìm kiếm tức thì và trộn lẫn vai trò:** [`_Layout.cshtml`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml) dùng thanh `#090909` viền cứng cố định thay vì chuyển mượt từ gradient trong suốt trên Hero sang nền đặc khi cuộn; chưa có ô tìm kiếm ngay trên header; đưa cả mục nội bộ (`Soát vé`, `Vận hành`) vào chung cấu trúc menu khách hàng. | `[Đã xác minh trực tiếp bằng trình duyệt]` + `[Đã đọc mã nguồn]` `_Layout.cshtml`, `app-shell.css`. | Giảm tính đắm chìm trên trang chủ và giảm mật độ làm việc chuyên nghiệp ở khu vực vận hành nội bộ. |
| **P2 (Nhất quán thời gian & Hộp thoại)** | **Trộn lẫn giờ `UTC` thô và giờ `UTC+7` trên màn hình khách hàng; dùng `window.confirm()`:** [`Orders/Details.cshtml`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Views/Orders/Details.cshtml#L66) in `CreatedUtc ... UTC` ngay cạnh giờ chiếu Việt Nam; [`Orders/Tickets.cshtml`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Views/Orders/Tickets.cshtml#L99) in `IssuedUtc` không ghi múi giờ; nút hủy đơn/mô phỏng lỗi gọi hộp thoại `confirm()` mặc định của trình duyệt. | `[Đã xác minh trực tiếp bằng trình duyệt]` + `[Đã đọc mã nguồn]`. | Gây bối rối cho khách hàng Việt Nam khi thấy giờ phát hành vé lệch 7 tiếng so với đồng hồ thực tế. |

### 1.4. Kiểm kê Toàn bộ Màn hình & Đánh giá `Giữ` / `Tinh chỉnh` / `Thay thế`

| Nhóm | Route / View / Tài nguyên | Quyết định | Lý do & Hướng xử lý |
| :--- | :--- | :--- | :--- |
| **Backend & Nghiệp vụ** | Toàn bộ 9 API Controllers, Services đặt giữ ghế, tính tiền, QR AES-GCM, HMAC Payment, `Runner` snapshot | **GIỮ (Mở rộng mỏng)** | Đã đạt chuẩn toàn vẹn dữ liệu; chỉ mở rộng DTO/schema cho metadata phim thật và thêm 3 endpoint đọc/đồng bộ mỏng. |
| **Logic thời gian & URL** | Gom nhóm suất chiếu `DateTimeOffset (+07:00)`, query filter `/movies?q=&filter=`, `?date=` | **GIỮ & TINH CHỈNH** | Logic đúng đắn; giữ nguyên hợp đồng URL và nâng cấp giao diện + bổ sung lọc theo thể loại/trạng thái. |
| **Layout & CSS nền** | `_Layout.cshtml`, `design-tokens.css`, `app-shell.css`, `dark-compat.css`, `site.css` | **THAY THẾ THEO GIAI ĐOẠN** | Chuyển sang hệ CSS độc lập `wwwroot/css/cinema/*` và tách `_CinemaLayout.cshtml` / `_OperationsLayout.cshtml`; gỡ bỏ hoàn toàn `dark-compat.css` ở Giai đoạn 8. |
| **Khám phá phim** | `Home/Index.cshtml`, `Movies/Index.cshtml`, `Movies/Details.cshtml`, `home-cinema.css`, `movie-discovery.css` | **TINH CHỈNH SÂU / NÂNG CẤP** | Thay toàn bộ artwork SVG giả bằng dữ liệu & ảnh phim thật; nâng cấp Hero có vùng chữ an toàn, thêm hàng 16:9 kèm giờ chiếu nhanh, Hover Quick-Preview, Trailer Modal và bảng nguồn điểm đánh giá. |
| **Đặt ghế** | `Showtimes/Seats.cshtml` | **THAY THẾ GIAO DIỆN** | Chuyển ghế sang `<button type="button" aria-pressed>` hỗ trợ phím mũi tên 2D, đa dấu hiệu (`★`, `✓`, sọc chéo `⏳`, `✕`), bỏ tooltip tối lỗi và bỏ `alert()`. |
| **Đơn hàng, Checkout & Vé** | `Orders/Index.cshtml`, `Orders/Details.cshtml`, `Orders/Checkout.cshtml`, `Orders/Tickets.cshtml` | **THAY THẾ GIAO DIỆN** | Bỏ sạch `.bg-primary`/`#0d6efd`, đồng bộ 100% thời gian sang `UTC+7`, thiết kế lại vé điện tử dạng cuống vé đục lỗ có vùng QR trắng chuẩn, dùng `<dialog>` thay `confirm()`. |
| **Tài khoản & Trang phụ** | `Account/Login.cshtml`, `Profile.cshtml`, `AccessDenied.cshtml`, `Home/Privacy.cshtml`, `Shared/Error.cshtml` | **THAY THẾ GIAO DIỆN** | Thiết kế form đăng nhập tối sang trọng kiểu Netflix (tập trung giao dịch, giữ trọn `returnUrl`, không giả làm landing page bán gói tháng). |
| **Vận hành & Quản trị** | `StaffCheckIn/Index.cshtml`, `History.cshtml`, `Management/Movies*.cshtml`, `Showtimes*.cshtml`, `Rooms*.cshtml`, `Reports.cshtml` | **THAY THẾ SANG `_OperationsLayout`** | Chuyển sang giao diện bàn làm việc tối mật độ cao: bảng sắc nét, lọc nhanh, phân tách nút nguy hiểm, bổ sung công cụ tra cứu/đồng bộ phim thật từ TMDB/Wikidata. |

---

## Phần 2. Phương án Thiết kế Chủ đạo: “Netflix-Caliber Cinema Box Office”

### 2.1. Giao diện sẽ Giống Netflix ở Những Đặc điểm Cụ thể Nào?
Không đánh giá “giống Netflix” bằng việc tô nền đen và nút đỏ, mà áp dụng chính xác **8 quy luật thị giác và tương tác đặc trưng của Netflix**:

1. **Chiều sâu bề mặt tối đa lớp (Layered Obsidian Surfaces):** Chuyển sắc tinh tế giữa nền trang `#0A0A0C`, bề mặt thẻ `#16161B`, bề mặt nổi `#1E1E25` và thẻ preview nổi `#18181F` có viền sáng vi mô `1px solid rgba(255,255,255,0.12)` giúp tách lớp không cần đường kẻ thô.
2. **Header chuyển trạng thái thông minh:** Ở đỉnh Trang chủ và Chi tiết phim, thanh header phủ lớp gradient tối nhẹ bên trên ảnh nền để hình ảnh tràn sát mép trên màn hình; ngay khi cuộn quá `32px`, header chuyển mượt sang nền kính mờ `#0A0A0C` (`95%` opacity + `backdrop-filter: blur(14px)`), kèm ô tìm kiếm mở rộng trực tiếp ngay trên thanh điều hướng.
3. **Hero toàn chiều ngang với Vùng Chữ An toàn (Left 42% Safe Zone) & Vignette Kép:** Ảnh backdrop `16:9` thật chiếm trọn chiều ngang, căn điểm nhìn nhân vật sang nửa phải (`object-position: 72% 22%`), kết hợp hai lớp gradient (trái sang phải bảo vệ độ tương phản chữ `>= 10:1` và dưới lên trên hòa mượt chân ảnh vào hàng phim đầu tiên).
4. **Hàng nội dung ngang kéo chồng lên chân Hero (`margin-top: -54px`):** Hàng phim đầu tiên nhô nhẹ lên phần chân tối của Hero để người dùng trên màn hình `1440×900` lập tức nhìn thấy nội dung khám phá tiếp theo mà không bị “tường ảnh” che kín màn hình.
5. **Hai định dạng khung hình chuẩn điện ảnh:** Khung ngang `16:9` giàu ngữ cảnh cảnh phim cho hàng “Đang mở bán vé hôm nay” và Hover Preview; khung dọc `2:3` chuẩn poster rạp chiếu cho hàng tuyển tập và lưới danh mục.
6. **Thẻ Xem Nhanh Nổi (Hover / Focus Quick-Preview Card có trễ `320ms`):** Khi dừng chuột có chủ đích `320ms` hoặc kích hoạt xem nhanh bằng bàn phím trên card phim ở desktop, một thẻ preview mở rộng êm ái hiển thị ảnh cảnh phim `16:9`, điểm đánh giá kèm nguồn, thời lượng, đạo diễn và các nút chọn nhanh giờ chiếu mà không đẩy lệch bố cục các hàng xung quanh.
7. **Hệ Typography mạnh, nén chặt ở tiêu đề lớn và thoáng dấu ở nội dung tiếng Việt:** Sử dụng phông chữ không chân hiện đại thiết kế tối ưu cho dấu tiếng Việt (**`Be Vietnam Pro`**) kết hợp chữ đơn cách (**`JetBrains Mono`**) cho khung giờ chiếu, mã ghế, đồng hồ giữ chỗ và mã đơn/vé.
8. **Phân biệt rõ trang xác thực với không gian duyệt nội dung:** Trang `/account/login` là hộp thoại xác thực tập trung để hoàn tất giữ ghế/quản lý vé — tuyệt đối không biến thành trang chào mời đăng ký gói cước truyền hình.

### 2.2. Những Phần Điều chỉnh Riêng để Phục vụ Đặt Vé Tại Rạp Beta Giải Phóng
1. **CTA chính luôn hướng tới Suất chiếu & Đặt vé vật lý:** Thay nút `▶ Phát (Play)` của nền tảng streaming bằng **`Đặt vé • Chọn suất chiếu`** (khi `UpcomingShowtimesCount > 0`) hoặc **`Xem thông tin • Chưa mở lịch`** (khi chưa có suất).
2. **Chip Giờ Chiếu Nhanh ngay trên Hàng Phim Mở Bán:** Trên hàng đầu tiên của Trang chủ, mỗi phim mở bán hôm nay hiển thị trực tiếp tối đa 3 khung giờ gần nhất (`19:15`, `21:30`, `23:15 (+1d)`) để khách quen rạp bấm 1 chạm vào thẳng sơ đồ ghế.
3. **Tách biệt Dữ liệu Tác phẩm và Dữ liệu Rạp:** Mọi điểm số ghi rõ nguồn (`IMDb 7.8/10 • 95.223 lượt`), mọi nhãn lịch chiếu gắn với bảng `dbo.Showtimes` thực tế và có ghi chú học thuật gọn gàng không gây rối mắt.
4. **Ưu tiên Độ Rõ Ràng Tuyệt đối ở Sơ đồ Ghế, Thanh toán & Vé QR:** Khi khách đã bước vào `/showtimes/{id}/seats`, `/checkout` và `/tickets`, giao diện giảm hoàn toàn hiệu ứng chuyển động trang trí để ưu tiên độ sắc nét của mã ghế, tổng tiền VND, đồng hồ giữ chỗ server và vùng trắng bảo vệ mã QR.

---

## Phần 3. Đặc tả Hệ thống Thiết kế (`Design System Specification`)

### 3.1. Semantic Color Tokens & Độ Tương phản WCAG

| Nhóm Token | Tên biến CSS | Giá trị | Ngữ nghĩa sử dụng & Tỷ lệ tương phản |
| :--- | :--- | :--- | :--- |
| **Bề mặt (Surfaces)** | `--bgp-bg-canvas` | `#0A0A0C` | Nền gốc toàn trang dành cho khách hàng. |
| | `--bgp-bg-surface-1` | `#16161B` | Nền thẻ phim, khung phòng chiếu, bảng tóm tắt đơn, khối dữ liệu. |
| | `--bgp-bg-surface-2` | `#1E1E25` | Ô nhập liệu, nút chọn suất chiếu, dòng ghế đã chọn, chip phụ. |
| | `--bgp-bg-surface-3` | `#272731` | Trạng thái hover của bề mặt cấp 2 và skeleton nền. |
| **Chữ (Typography)** | `--bgp-text-primary` | `#F5F5F7` | Tiêu đề, tên phim, tổng tiền (`18.1:1` trên `#0A0A0C` — đạt AAA). |
| | `--bgp-text-secondary` | `#B8B8C2` | Mô tả phim, metadata phụ, nhãn bảng (`10.1:1` trên `#0A0A0C` — đạt AAA). |
| | `--bgp-text-muted` | `#8A8A98` | Ghi chú nguồn, chú thích phụ (`5.6:1` trên `#0A0A0C`, `4.7:1` trên `#16161B` — đạt AA). |
| **Thương hiệu & Điểm nhấn** | `--bgp-brand-red` / `-hover` | `#E50914` / `#F40612` | Nút CTA chính, gạch chân điều hướng đang mở, ghế đang chọn (`4.9:1` với chữ `#FFFFFF` đậm). |
| | `--bgp-accent-gold` | `#F5C518` | Huy hiệu nguồn điểm IMDb và viền nhận diện hạng ghế VIP. |
| **Trạng thái Nghiệp vụ** | `--bgp-status-success` | `#22C55E` (`#86EFAC` chữ) | Đơn `PAID`, vé `ISSUED` hợp lệ, kết quả soát vé cho phép vào phòng. |
| | `--bgp-status-warning` | `#F59E0B` (`#FCD34D` chữ) | Đơn `PENDING_PAYMENT`, ghế `HELD`, suất sắp hết ghế, `NEEDS_REVIEW`. |
| | `--bgp-status-danger` | `#EF4444` (`#FCA5A5` chữ) | Thanh toán `FAILED`, đơn `CANCELLED`, từ chối soát vé đã dùng/sai chữ ký, hành động nguy hiểm. |

### 3.2. Hệ Typography Hỗ trợ Tiếng Việt
- **Phông chữ chính (`--bgp-font-sans`):** `'Be Vietnam Pro', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif` (nạp file `.woff2` subset `vietnamese + latin` lưu cục bộ tại `wwwroot/fonts/` khi triển khai để không phụ thuộc mạng ngoài và không bị nháy phông).
- **Phông chữ số liệu & mã định danh (`--bgp-font-mono`):** `'JetBrains Mono', ui-monospace, Consolas, monospace` kèm `font-variant-numeric: tabular-nums` bắt buộc cho mọi con số tiền tệ VND, đếm ngược `mm:ss`, giờ chiếu `HH:mm` và mã ghế `A1..E10`.
- **Thang chữ (Type Scale):**
  - `Hero Display`: `clamp(2.25rem, 4.2vw, 3.65rem)`, `font-weight: 800`, `line-height: 1.08` (đủ khoảng thở cho dấu kép tiếng Việt như *“Mê Trong Mê”*, *“Đào, Phở và Piano”*).
  - `Page H1`: `clamp(1.6rem, 2.5vw, 2.35rem)`, `font-weight: 800`, `line-height: 1.18`.
  - `Rail / Section H2`: `1.2rem – 1.35rem`, `font-weight: 700`, `line-height: 1.3`.
  - `Body`: `0.95rem` (`15.2px`–`16px`), `line-height: 1.6`.
  - `Caption / Metadata`: tối thiểu `0.75rem` (`12px`), không dùng cỡ chữ nhỏ hơn `11px` cho bất kỳ thông tin nghiệp vụ nào.

### 3.3. Bố cục, Spacing, Breakpoints, Radius & Shadow
- **Lưới khoảng cách:** Bội số `4px` (`4, 8, 12, 16, 20, 24, 32, 40, 48, 64px`).
- **Container linh hoạt:**
  - Trang khám phá (`Home`, `Catalog`, `Movie Detail`): Fluid container với lề ngang `--bgp-gutter: clamp(16px, 4vw, 56px)`, giới hạn chiều rộng tối đa `1680px` cho màn hình siêu rộng.
  - Trang giao dịch tập trung (`Seat Selection`, `Order Detail`, `Checkout`, `Tickets`): `max-width: 1240px` căn giữa giúp mắt bao quát sơ đồ ghế và bảng giá nhanh nhất.
  - Trang vận hành (`_OperationsLayout`): Khung 2 cột `240px Sidebar + Fluid Main Pane` tối ưu bảng nhiều cột.
- **Breakpoints chuẩn:** Mobile nhỏ `320px`, Mobile chuẩn `390px`, Phablet/Tablet nhỏ `576px`, Tablet `768px`, Desktop `992px`, Widescreen `1280px` & `1440px+`.
- **Bo góc & Đổ bóng:** Card poster/backdrop dùng bo góc sắc sảo kiểu điện ảnh (`6px`), hộp thoại/khung lớn (`10px–14px`), chip bộ lọc (`999px`). Đổ bóng chỉ dùng cho lớp nổi thực sự (`Popover`, `Dialog`, `Sticky Dock`).

### 3.4. Quy chuẩn Component, Trạng thái Loading/Empty/Error & Khả năng Tiếp cận (A11y)
- **Buttons:** Chiều cao tối thiểu `44px` trên cảm ứng (`36px` chỉ dành cho nút phụ trong bảng quản trị desktop); phân cấp rõ 4 biến thể: `Primary Red` (hành động chính duy nhất trên màn hình), `Frosted Glass` (hành động phụ trên Hero), `Subtle Outline` (điều hướng phụ) và `Danger Outline / Solid` (hủy đơn, khóa phim, hủy suất chiếu).
- **Skeletons, Empty & Error States:**
  - *Loading:* Khung giữ chỗ đúng tỷ lệ `2:3` / `16:9` với hiệu ứng ánh sáng quét ngang nhẹ (`.bgp-skeleton`), tắt chuyển động khi bật `prefers-reduced-motion: reduce`.
  - *Empty:* Phân biệt rõ “hệ thống chưa có dữ liệu” với “không khớp bộ lọc hiện tại” (luôn đi kèm nút 1 chạm xóa bộ lọc).
  - *Error:* Thông báo lỗi đặt ngay sát vị trí phát sinh (ví dụ ghế vừa bị người khác giữ mất khi bấm đặt sẽ đánh dấu lại sơ đồ ghế và mời chọn ghế khác thay vì văng trang lỗi chung chung).
- **Chuẩn tiếp cận bàn phím & Cảm ứng:**
  - Vòng nét đứt/kép `:focus-visible` (`0 0 0 2px #0A0A0C, 0 0 0 4px #F5F5F7`) nổi bật trên cả nền đen lẫn nền đỏ.
  - Mục tiêu chạm trên mobile (`Search`, `Filter Pill`, `Date Tab`, `Seat Button`, `Bottom Nav`) đạt tối thiểu `44×44px` (riêng sơ đồ ghế nhiều cột trên màn hình `320px–390px` hỗ trợ khung cuộn ngang mượt có chỉ báo vị trí + nút thu phóng `100% / Vừa màn hình` để không ép nút ghế xuống nhỏ hơn kích thước bấm chính xác).

---

## Phần 4. Thiết kế Hành trình Khách hàng & Bàn làm việc Vận hành (Kèm Prototype Đã Render)

Toàn bộ 6 màn hình trọng tâm đã được hiện thực hóa thành bộ **Standalone HTML/CSS/JS Prototype** trong [`docs/ui/prototype/`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/README.md) và kết xuất bằng trình duyệt thực tế tại [`docs/ui/prototype/screenshots/`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/screenshots/):

1. **Trang chủ Desktop ([`01-home-desktop.html`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/01-home-desktop.html) → [`01-home-desktop.png`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/screenshots/01-home-desktop.png)):**
   - Hero dùng ảnh cảnh phim Criterion `Public Domain` độ phân giải cao của *Mê trong mê (Charade, 1963)*, toàn bộ khối chữ nằm gọn trong vùng tối an toàn bên trái, hiển thị `IMDb 7.8/10 (95.223 lượt)`, thời lượng, đạo diễn, diễn viên và CTA kép.
   - Hàng 1 (`Đang mở bán vé hôm nay`) dùng thẻ ngang `16:9` kèm chip giờ chiếu trực tiếp và minh họa trạng thái **Hover Quick-Preview Card** trên phim *Charge (2022)*. Hàng 2 dùng thẻ poster `2:3` chuẩn rạp của 7 tác phẩm độc lập — tuyệt đối không nhân bản phim; khi hệ thống có ít phim (ví dụ chỉ 2–3 phim), bố cục tự động chuyển sang chế độ **Featured Spotlight Grid** lớn thay vì để hàng ngang trống trải.
2. **Chi tiết Phim & Chọn Suất `UTC+7` ([`02-movie-detail-desktop.html`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/02-movie-detail-desktop.html) → [`02-movie-detail-desktop.png`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/screenshots/02-movie-detail-desktop.png)):**
   - Kết hợp backdrop khí quyển phía sau với poster `2:3` nguyên vẹn bên trái, thẻ diễn viên & nhân vật, nút mở hộp thoại `<dialog>` phát Trailer YouTube chính thức (`youtube-nocookie.com`), bảng chọn ngày & suất chiếu `UTC+7` phân biệt rõ suất còn nhiều ghế / sắp hết / hết ghế / qua nửa đêm (`+1 ngày`), và hộp **Hồ sơ Nguồn Dữ liệu Tác phẩm** bên phải. Nếu phim chưa có suất chiếu, khối lịch chiếu hiển thị thông báo rõ ràng kèm gợi ý các phim đang mở bán hôm nay.
3. **Khám phá Phim Mobile `390×844` ([`03-catalog-mobile.html`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/03-catalog-mobile.html) → [`03-catalog-mobile.png`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/screenshots/03-catalog-mobile.png)):**
   - Thiết kế riêng cho ngón cái với thanh tìm kiếm + chip lọc dính ở đầu trang, lưới 2 cột chuẩn `2:3`, thanh điều hướng đáy 4 mục và minh họa trực quan thẻ phim Việt Nam *Mai (2024 — `T18`, `IMDb 6.9/10`)* sử dụng placeholder trung tính **`Chưa có hình ảnh`** khi chờ cấu hình `BGP_TMDB_READ_TOKEN`.
4. **Chọn Ghế Đa Dấu Hiệu ([`04-seat-selection-desktop.html`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/04-seat-selection-desktop.html) → [`04-seat-selection-desktop.png`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/screenshots/04-seat-selection-desktop.png)):**
   - Thanh tiến trình 4 bước rõ ràng, sơ đồ ghế `<button aria-pressed>` có lối đi dọc, phân biệt 5 trạng thái bằng viền + nền + ký hiệu (`★` VIP, `✓` Đang chọn, nền sọc chéo + `⏳` Đang giữ, `✕` Đã bán), bảng tóm tắt dính bên phải với poster nhỏ và ghi rõ **chưa đếm ngược trước khi máy chủ xác nhận giữ ghế**.
5. **Checkout & Vé Điện tử QR ([`05-checkout-desktop.html`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/05-checkout-desktop.html) → [`05-checkout-desktop.png`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/screenshots/05-checkout-desktop.png)):**
   - Thanh cảnh báo đếm ngược lấy từ `HoldExpiresUtc` của server, bảng đối chiếu ghế/giá VND nguyên, mẫu vé điện tử đục lỗ với vùng QR nền trắng `#FFFFFF` tuyệt đối không áp bộ lọc tối, và khung **Trình mô phỏng Phản hồi Cổng Thanh toán (MIS)** trung thực không giả lập ô nhập thẻ tín dụng.
6. **Console Vận hành Nội bộ ([`06-operations-desktop.html`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/06-operations-desktop.html) → [`06-operations-desktop.png`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/screenshots/06-operations-desktop.png)):**
   - Khung `_OperationsLayout` cho `MANAGER`/`ADMIN` (bảng danh mục phim mật độ cao hiển thị mã `QID`/`IMDb`/`TMDB`, trạng thái khóa ghi đè thủ công `Manual Override`, trạng thái `BGP_TMDB_READ_TOKEN`, nút nhập phim theo ID nguồn ngoài) kết hợp chuẩn thẻ phản hồi quét vé QR độ tương phản cao cho `STAFF` (`HỢP LỆ — CHO PHÉP VÀO RẠP` vs `TỪ CHỐI — VÉ ĐÃ SỬ DỤNG`).

---

## Phần 5. Roadmap Triển khai Theo 8 Giai đoạn Độc lập (`Incremental Delivery Roadmap`)

Mỗi giai đoạn dưới đây là một PR độc lập, giữ toàn bộ hệ thống biên dịch sạch và vượt qua 100% bài kiểm thử xUnit + Playwright hiện hữu trước khi sang giai đoạn kế tiếp.

### Giai đoạn 1 (`UI-P1`): Nền tảng Design System Điện ảnh & Kiến trúc Hai Khung Giao diện (`Dual-Shell`)
- **Mục tiêu:** Thiết lập bộ CSS Netflix-Cinema độc lập (không phụ thuộc Bootstrap trên màn hình mới), phông chữ tiếng Việt cục bộ và tách bạch hai khung `_CinemaLayout.cshtml` (Khách hàng) vs `_OperationsLayout.cshtml` (Nội bộ).
- **Thuộc phạm vi:** `wwwroot/css/cinema/{tokens,base,shell-cinema,shell-ops,components}.css`, `wwwroot/fonts/BeVietnamPro-*`, `wwwroot/js/modules/shell-nav.js`, `_CinemaLayout.cshtml`, `_OperationsLayout.cshtml`, cập nhật điều kiện nạp Bootstrap legacy cho các màn hình cũ, trang `Home/Privacy.cshtml`, `Shared/Error.cshtml`, `Account/AccessDenied.cshtml`.
- **Không thuộc phạm vi:** Chưa thay đổi schema DB, chưa đồng bộ dữ liệu phim ngoài, chưa viết lại nội dung bên trong `Seats`, `Checkout`, `Tickets` hay bảng `Management`.
- **Thiết kế chính:** Header khách hàng chuyển mượt trong suốt → kính mờ `#0A0A0C`, ô tìm kiếm trượt ngang ngay trên header (`GET /movies?q=...`), drawer di động gọn sạch; Sidebar/Topbar vận hành riêng cho `/staff/*` và `/management/*`.
- **Dữ liệu / API / Asset cần có:** Bộ file font `.woff2` `Be Vietnam Pro` & `JetBrains Mono` giấy phép mở `OFL-1.1` lưu trong `wwwroot/fonts/`.
- **Thay đổi FE / BE:** Chỉ thay đổi tầng Razor Layout, static assets và `ViewData["UseLegacyBootstrap"]`; giữ nguyên 100% Controllers và Backend.
- **Phụ thuộc:** Xuất phát trực tiếp từ baseline `main` (`4c0a3830`).
- **Tiêu chí nghiệm thu:** Header khách hàng không còn mục nội bộ cho khách/CUSTOMER; ô tìm kiếm trên header điều hướng đúng tới `/movies?q=...`; các trang cũ chưa nâng cấp vẫn hiển thị bình thường không vỡ bố cục; không có cuộn ngang ở `320px`, `390px`, `768px`, `1440px`.
- **Kiểm thử:** Chạy đủ 166 test xUnit + 16 spec Playwright hiện có + thêm `09-dual-shell-navigation.spec.ts` kiểm tra chuyển trạng thái cuộn header, tìm kiếm nhanh, phân tách menu theo vai trò (`Guest`, `CUSTOMER`, `STAFF`, `MANAGER`, `ADMIN`).
- **Screenshot nghiệm thu:** Header đỉnh trang vs sau khi cuộn (desktop/mobile), ô tìm kiếm mở rộng, khung `_OperationsLayout` ở `STAFF` và `MANAGER`.
- **Rủi ro & Rollback:** Rủi ro xung đột selector giữa CSS mới và Bootstrap trên trang chưa chuyển đổi → Giải quyết bằng tiền tố nghiêm ngặt `.bgp-*` và cờ nạp layout riêng; rollback gọn bằng 1 lệnh revert PR.

### Giai đoạn 2 (`BE/DATA-P2`): Schema Phim Thật, Media Pipeline Cục bộ & Bộ Dữ liệu Mẫu Đã Xác minh
- **Mục tiêu:** Mở rộng mô hình dữ liệu `Movie` để lưu trữ đầy đủ metadata phim thật có nguồn xác minh, xây dựng `MovieContentSyncService` + lệnh `Runner` nạp bộ 11 phim thật đã kiểm chứng và cô lập hoàn toàn dữ liệu giả khỏi giao diện trình diễn.
- **Thuộc phạm vi:** Entity `Movie` (+ tùy chọn `MovieCredit`), EF Core Migration mới, `BetaModelConfiguration.cs`, `sql/01_reference_schema_SQL_ONLY.sql`, `Data_Dictionary`, `Foreign_Key_Matrix`, `scripts/static_schema_checks.py`, `CatalogService`, `MovieDtos`, `MovieContentSyncService` (hỗ trợ bộ dữ liệu mở đã xác minh + tích hợp `BGP_TMDB_READ_TOKEN` & `IMDb TSV` khi cấu hình), thư mục ảnh tối ưu đa kích thước và tách lệnh `seed-verified-showcase` khỏi `seed-ui-fixtures` tổng hợp.
- **Không thuộc phạm vi:** Chưa thiết kế lại giao diện Sơ đồ ghế, Checkout, Vé hay Console quản trị (ngoài việc hiển thị an toàn các cột mới không lỗi).
- **Thiết kế chính:** Thiết lập chuẩn Placeholder trung tính `"Chưa có hình ảnh"` thay cho toàn bộ poster vector giả (`poster-scifi.svg`...); chuẩn hóa DTO trả về đối tượng `MovieRatingDto` (`Source`, `Score`, `ScaleMax`, `VoteCount`, `FetchedUtc`) và `MovieArtworkDto`.
- **Dữ liệu / API / Asset cần có:** Bộ 11 phim đã xác minh trong [`REAL_MOVIE_CONTENT_PLAN.md`](file:///d:/Projects/BetaGiaiPhong/docs/ui/REAL_MOVIE_CONTENT_PLAN.md) + 20 asset hợp pháp đa kích thước (`w342`, `w780`, `w1280`) kèm manifest bản quyền; cấu hình tùy chọn `BGP_TMDB_READ_TOKEN` qua User Secrets / biến môi trường.
- **Thay đổi FE / BE:** Mở rộng Domain/Infrastructure/Application, cập nhật `Runner`, bổ sung endpoint đọc nhẹ `GET /api/movies/{id}/quick-preview`.
- **Phụ thuộc:** Sau `UI-P1` (hoặc song song độc lập).
- **Tiêu chí nghiệm thu:** `python scripts/static_schema_checks.py` PASS tuyệt đối; `Runner verify` PASS `0 violation(s)`; mọi truy vấn phim trả đúng tên gốc, năm, đạo diễn, diễn viên, trailer, nguồn điểm và giấy phép ảnh; không còn bất kỳ tên phim hư cấu hay poster SVG giả nào trong dữ liệu trình diễn mặc định.
- **Kiểm thử:** Unit test + Integration test cho `MovieContentSyncService` (kiểm tra khóa `manual_override_mask`, xử lý thiếu trường, lỗi mạng nguồn ngoài giữ nguyên cache cũ, loại phim thiếu backdrop khỏi ứng viên Hero).
- **Screenshot nghiệm thu:** Kết quả JSON `/api/movies`, `/api/movies/{id}/quick-preview` và ảnh chụp trang danh mục hiển thị dữ liệu phim thật + placeholder trung tính cho phim chờ khóa TMDB.
- **Rủi ro & Rollback:** Rủi ro lệch số đếm cột trong `static_schema_checks.py` → Cập nhật đồng bộ SQL tham chiếu, Data Dictionary và script kiểm tra ngay trong cùng commit; các cột mới đều `NULL`-able nên tương thích ngược hoàn toàn.

### Giai đoạn 3 (`UI-P3`): Trải nghiệm Khám phá Điện ảnh — Trang chủ, Danh mục, Chi tiết Phim, Hover Preview & Trailer
- **Mục tiêu:** Đưa Trang chủ (`/`), Danh mục (`/movies`) và Chi tiết phim (`/movies/{id}`) lên chất lượng thị giác Netflix hoàn chỉnh trên nền `_CinemaLayout` (tắt hẳn Bootstrap trên 3 trang này).
- **Thuộc phạm vi:** `Views/Home/Index.cshtml`, `Views/Movies/Index.cshtml`, `Views/Movies/Details.cshtml`, `wwwroot/css/cinema/page-{home,catalog,detail}.css`, `wwwroot/js/modules/{rail-carousel,preview-popover,trailer-dialog,showtime-picker}.js`.
- **Không thuộc phạm vi:** Sơ đồ ghế, Checkout, Vé QR và khu vực Quản trị.
- **Thiết kế chính:** Hero toàn chiều ngang với vùng chữ an toàn & vignette kép; hàng phim mở bán hôm nay `16:9` kèm chip giờ chiếu nhanh; Hover/Focus Quick-Preview trễ `320ms`; lưới poster `2:3` dùng `srcset`/`sizes`; chế độ bố cục ít phim không nhân bản card; hộp thoại `<dialog>` phát trailer YouTube chính thức click-to-load; khu vực chọn suất `UTC+7` nổi bật tách biệt với hồ sơ nguồn dữ liệu tác phẩm.
- **Dữ liệu / API / Asset cần có:** Sử dụng đầy đủ DTO & ảnh thật từ Giai đoạn 2 + JSON Island preview.
- **Thay đổi FE / BE:** Cập nhật `HomeController` và `MoviesController` để lọc ứng viên Hero đủ điều kiện (có backdrop thật + synopsis) và cấp dữ liệu hàng phim theo chủ đề có căn cứ dữ liệu thật.
- **Phụ thuộc:** Cần hoàn thành `UI-P1` và `BE/DATA-P2`.
- **Tiêu chí nghiệm thu:** Ảnh Hero có `fetchpriority="high"`; không xuất hiện bất kỳ nhãn giả (`"98% phù hợp"`, `"Top 10"`); điểm đánh giá luôn kèm tên nguồn + thang + lượt bình chọn; URL bộ lọc `/movies?q=&filter=` và `/movies/{id}?date=` giữ nguyên trạng thái khi F5 / Back; tắt JS vẫn xem được phim và bấm chọn ngày chiếu.
- **Kiểm thử:** Nâng cấp `07-cinematic-home.spec.ts`, `08-movie-discovery.spec.ts` và kiểm tra `prefers-reduced-motion`.
- **Screenshot nghiệm thu:** Desktop `1440×900`, Tablet `768×1024`, Mobile `390×844` & `320×568` cho `/`, `/movies` (đủ phim, lọc rỗng, placeholder thiếu ảnh), `/movies/{id}` (có suất, hết ghế, qua nửa đêm, chưa có suất, mở modal trailer).
- **Rủi ro & Rollback:** Rủi ro thẻ Hover Preview bị tràn mép phải màn hình → Module `preview-popover.js` đo `getBoundingClientRect()` để tự căn lề biên trái/phải và tự vô hiệu hóa trên thiết bị cảm ứng (`hover: none`).

### Giai đoạn 4 (`UI-P4`): Sơ đồ Ghế Điện ảnh Đa Dấu hiệu & Tương tác Bàn phím Toàn diện
- **Mục tiêu:** Thiết kế lại hoàn toàn `/showtimes/{id}/seats` trên `_CinemaLayout`, giải quyết triệt để lỗi P0 về bàn phím, độ tương phản và phân biệt trạng thái ghế mà không đánh đổi tốc độ chọn ghế.
- **Thuộc phạm vi:** `Views/Showtimes/Seats.cshtml`, `wwwroot/css/cinema/page-seat-map.css`, `wwwroot/js/modules/seat-map.js`.
- **Không thuộc phạm vi:** Thay đổi thuật toán khóa ghế ở backend hay màn hình Checkout.
- **Thiết kế chính:** Thanh tiến trình đặt vé; vòm màn hình chiếu tinh tế; mỗi ghế là `<button type="button" class="bgp-seat" aria-pressed="false">` hỗ trợ điều hướng mũi tên 2 chiều (`Roving Tabindex`); 5 trạng thái phân biệt bằng hình khối + viền + ký hiệu (`★` VIP, `✓` Đang chọn, sọc chéo + `⏳` Đang giữ, `✕` Đã bán); thanh tóm tắt dính hiển thị poster thật + danh sách ghế + tổng tiền VND nguyên; thay `window.alert()` khi chọn quá 8 ghế bằng thông báo nổi nội tuyến (`aria-live="polite"`); **tuyệt đối không hiển thị đồng hồ đếm ngược ở bước này**.
- **Dữ liệu / API / Asset cần có:** Tái sử dụng `SeatMapDto` + bổ sung ảnh poster nhỏ và thời lượng phim từ `CatalogService`.
- **Thay đổi FE / BE:** Chủ yếu ở View và ES Module `seat-map.js`; tùy chọn lưu nháp mã ghế vào `sessionStorage` khi khách chưa đăng nhập bấm chuyển sang `/account/login?returnUrl=...`.
- **Phụ thuộc:** Sau `UI-P1` và `BE/DATA-P2`.
- **Tiêu chí nghiệm thu:** Đặt chọn/bỏ chọn ghế hoàn toàn bằng bàn phím (`Tab` + phím mũi tên + `Space`/`Enter`); phân biệt rõ ghế ngay cả khi bật bộ lọc mù màu (`grayscale`); hiển thị đúng trạng thái khóa cho `STAFF`/`MANAGER`/`ADMIN` hoặc suất chiếu đã đóng.
- **Kiểm thử:** Kiểm thử Playwright cho chọn ghế chuột + bàn phím, giới hạn 8 ghế không gọi `window.alert`, luồng khách chưa đăng nhập giữ `returnUrl`.
- **Screenshot nghiệm thu:** Sơ đồ ghế desktop `1440×900`, mobile `390×844` (chế độ cuộn ngang/thu phóng), trạng thái chọn ghế Thường + VIP, trạng thái chạm trần 8 ghế và suất chiếu đóng bán.
- **Rủi ro & Rollback:** Phòng chiếu nhiều cột trên màn hình `320px` → Khung cuộn ngang cục bộ có bóng báo biên (scroll shadow) giữ kích thước nút ghế dễ chạm, không làm tràn ngang toàn trang.

### Giai đoạn 5 (`UI-P5`): Giữ Ghế & Thanh toán Minh bạch (`Order Details` & `Checkout`)
- **Mục tiêu:** Thiết kế lại `/orders/{id}` và `/orders/{id}/checkout` đồng bộ tuyệt đối với ngôn ngữ tối điện ảnh, đồng hồ giữ chỗ chuẩn từ server và trình mô phỏng thanh toán học thuật trung thực.
- **Thuộc phạm vi:** `Views/Orders/Details.cshtml`, `Views/Orders/Checkout.cshtml`, `wwwroot/css/cinema/page-checkout.css`, `wwwroot/js/modules/hold-countdown.js`.
- **Không thuộc phạm vi:** Thay đổi giao thức HMAC-SHA256 hay cổng thanh toán backend.
- **Thiết kế chính:** Banner đếm ngược lấy từ `HoldExpiresUtc` (chống lệch nhịp tab nền bằng `performance.now()`); chuyển toàn bộ thời gian hiển thị sang `UTC+7` nhất quán; hộp thoại `<dialog>` xác nhận hủy giữ chỗ hoặc mô phỏng thanh toán thất bại (thay thế `window.confirm()`); khu vực **Trình mô phỏng Phản hồi Cổng Thanh toán (MIS)** thiết kế rõ ràng, trung thực, không giả lập ô nhập số thẻ ngân hàng; xử lý rõ các trạng thái `PENDING_PAYMENT`, `PAID`, `EXPIRED`, `CANCELLED`, `FAILED`, `NEEDS_REVIEW`.
- **Dữ liệu / API / Asset cần có:** Bổ sung `PosterUrl` và `CreatedVietnamText` vào `OrderDetailViewModel` / `CheckoutViewModel`.
- **Thay đổi FE / BE:** Mở rộng nhẹ ViewModel trong `OrdersController`, viết mới View & module đếm ngược.
- **Phụ thuộc:** Sau `UI-P1` và `UI-P4`.
- **Tiêu chí nghiệm thu:** Không còn màu `#0d6efd` hay `window.confirm()`; khi đồng hồ về `00:00` nút thanh toán tự khóa và hướng dẫn người dùng chọn lại ghế; mọi luồng thanh toán `SUCCESS` / `FAILED` / `Cancel` bảo toàn 100% kiểm tra toàn vẹn `Runner verify`.
- **Kiểm thử:** Playwright E2E cho trọn luồng giữ ghế → thanh toán thành công, thanh toán thất bại, hủy đơn qua hộp thoại `<dialog>` và đơn hết hạn.
- **Screenshot nghiệm thu:** `Checkout` đang giữ chỗ, hộp thoại xác nhận hủy đơn, `Order Details` ở các trạng thái `PAID`, `EXPIRED`, `FAILED`, `NEEDS_REVIEW`.
- **Rủi ro & Rollback:** Lệch đồng hồ máy trạm của khách → Chỉ dùng số giây còn lại do server tính (`HoldExpiresUtc - UtcNow`) tại thời điểm xuất trang, tuyệt đối không lấy `new Date()` của máy khách so sánh trực tiếp.

### Giai đoạn 6 (`UI-P6`): Vé Điện tử QR Độ Tương phản Cao, Lịch sử Đơn hàng & Tài khoản
- **Mục tiêu:** Hoàn thiện chặng cuối của khách hàng tại `/orders/{id}/tickets`, `/orders`, `/account/login` và `/account/profile` trên `_CinemaLayout`.
- **Thuộc phạm vi:** `Views/Orders/Tickets.cshtml`, `Views/Orders/Index.cshtml`, `Views/Account/Login.cshtml`, `Views/Account/Profile.cshtml`, `wwwroot/css/cinema/page-{tickets,account}.css` và `@media print` cho in vé.
- **Không thuộc phạm vi:** Máy quét vé phía nhân viên (`STAFF`).
- **Thiết kế chính:** Thẻ vé điện tử thiết kế dạng cuống vé rạp chiếu phim đục lỗ sang trọng, phân biệt rõ vé `ISSUED` (chưa soát) và `USED` (đã soát kèm thời gian quét `UTC+7`); **khung mã QR khóa cứng nền `#FFFFFF` + vùng trắng tĩnh `>= 10px`, `filter: none !important`** để máy quét đọc tức thì ở mọi độ sáng màn hình; trang Đăng nhập tối tinh gọn giữ trọn `returnUrl` và hộp chọn nhanh tài khoản demo chỉ hiển thị trong môi trường Development; danh sách đơn hàng dạng thẻ/bảng tối dễ tra cứu trên cả desktop và mobile.
- **Dữ liệu / API / Asset cần có:** Tái sử dụng `TicketsApiController` (`GET /api/tickets/{code}/qr`) và chuẩn hóa giờ phát hành sang `UTC+7`.
- **Thay đổi FE / BE:** Cập nhật Views và định dạng giờ Việt Nam trên `TicketDto` / ViewModel.
- **Phụ thuộc:** Sau `UI-P1` và `UI-P5`.
- **Tiêu chí nghiệm thu:** Ảnh QR giải mã thành công 100% trong E2E; không có bộ lọc tối hay độ mờ đè lên vùng QR; luồng đăng nhập giữ nguyên `returnUrl` và báo lỗi xác thực rõ ràng.
- **Kiểm thử:** E2E đặt vé → xuất vé QR → trích xuất ảnh QR từ DOM đưa thẳng vào API/luồng soát vé để xác minh khả năng đọc thực tế.
- **Screenshot nghiệm thu:** Trang Vé điện tử QR (desktop, mobile và chế độ in), Danh sách đơn hàng (có đơn & rỗng), Trang Đăng nhập và Hồ sơ tài khoản.
- **Rủi ro & Rollback:** CSS chế độ tối toàn cục vô tình ảnh hưởng `<img>` → Khóa cứng selector `.bgp-qr-box, .bgp-qr-box img { background: #fff !important; filter: none !important; opacity: 1 !important; }`.

### Giai đoạn 7 (`UI-P7`): Bàn làm việc Vận hành & Quản trị (`STAFF`, `MANAGER`, `ADMIN`) + Công cụ Đồng bộ TMDB
- **Mục tiêu:** Chuyển toàn bộ `/staff/*` và `/management/*` sang `_OperationsLayout` mật độ cao, nhanh, chuẩn xác, đồng thời cung cấp giao diện tra cứu & đồng bộ phim thật có kiểm soát cho `MANAGER`/`ADMIN`.
- **Thuộc phạm vi:** `StaffCheckIn/{Index,History}.cshtml`, `Management/{Movies,MovieCreate,MovieEdit,MovieDetail,Showtimes,ShowtimeCreate,ShowtimeDetail,Rooms,RoomDetail,Reports}.cshtml`, 2 API quản trị tra cứu/đồng bộ phim ngoài (`ExternalCatalogController` / `ManagementController`), `wwwroot/css/cinema/page-ops.css`, `wwwroot/js/modules/{ops-tmdb-sync,qr-scanner}.js`.
- **Không thuộc phạm vi:** Không biến bảng báo cáo hay quản lý suất chiếu thành tường poster giải trí.
- **Thiết kế chính:**
  - `STAFF` (`/staff/check-in`): Bàn trực cổng tốc độ cao với khung ngắm camera tối, nút tải ảnh QR được tạo kiểu đồng bộ (thay `<input type="file">` trắng mặc định), ô nhập mã vé thủ công và thẻ kết quả cỡ lớn phân biệt tức thì bằng màu + biểu tượng + âm báo nhẹ tùy chọn (`HỢP LỆ` xanh lá / `ĐÃ SỬ DỤNG` đỏ / `CHƯA ĐẾN GIỜ` vàng).
  - `MANAGER` / `ADMIN`: Bảng dữ liệu sắc nét có thanh lọc nhanh, phân tách rõ nút thao tác thường và nút nguy hiểm (`Lưu trữ phim`, `Hủy suất chiếu` mở hộp thoại `<dialog>` nêu rõ điều kiện nghiệp vụ), màn hình Tạo/Sửa phim tích hợp ô **“Tìm & Nhập từ TMDB / Wikidata / IMDb ID”** hiển thị bản xem trước dữ liệu thật, trạng thái cấu hình `BGP_TMDB_READ_TOKEN` và công tắc khóa ghi đè thủ công (`Manual Override`) cho từng nhóm trường.
- **Dữ liệu / API / Asset cần có:** Tích hợp `MovieContentSyncService` từ Giai đoạn 2 với quyền `[Authorize(Policy = "ManagementArea")]` + Anti-CSRF.
- **Thay đổi FE / BE:** Nâng cấp toàn bộ Views nội bộ sang `_OperationsLayout` và thêm action preview/sync metadata ngoài.
- **Phụ thuộc:** Sau `UI-P1` và `BE/DATA-P2`.
- **Tiêu chí nghiệm thu:** Nhân viên `STAFF` không truy cập được `/management/*` (`403`); Quản trị viên nhập phim thật sang `DRAFT`, rà soát nhãn tuổi VN rồi chuyển `PUBLISHED` thành công; khi chưa có `BGP_TMDB_READ_TOKEN`, giao diện báo trạng thái cấu hình rõ ràng không văng lỗi 500.
- **Kiểm thử:** E2E phân quyền `STAFF` / `MANAGER` / `ADMIN`, soát vé hợp lệ + quét lặp vé đã dùng + quản lý phim/suất chiếu/báo cáo.
- **Screenshot nghiệm thu:** Cổng soát vé `STAFF` (hợp lệ & từ chối), Lịch sử soát vé, Danh mục phim & Drawer đồng bộ nguồn ngoài, Lịch chiếu, Sơ đồ phòng và Báo cáo doanh thu.
- **Rủi ro & Rollback:** Thư viện quét camera trên trình duyệt không có quyền HTTPS/camera → Luôn duy trì song song 3 phương thức (Camera trực tiếp, Tải ảnh vé QR và Nhập mã vé thủ công).

### Giai đoạn 8 (`UI-P8`): Dọn dẹp Triệt để Legacy Bootstrap / `dark-compat.css` & Nghiệm thu Tổng thể Toàn Hệ thống
- **Mục tiêu:** Sau khi 100% (23/23) view đã chuyển sang `_CinemaLayout` và `_OperationsLayout`, gỡ bỏ hoàn toàn `bootstrap.min.css`, `bootstrap.bundle.min.js`, `_Layout.cshtml` cũ, `dark-compat.css` và các poster vector giả không còn dùng; thực hiện kiểm định chất lượng tổng thể (Visual, Content Provenance, Accessibility, Performance & Integrity).
- **Thuộc phạm vi:** Dọn dẹp thư mục `wwwroot/lib/bootstrap`, `wwwroot/css/dark-compat.css`, cập nhật tài liệu hướng dẫn cài đặt / demo bảo vệ đồ án và chạy bộ chụp nghiệm thu toàn trình.
- **Không thuộc phạm vi:** Thêm tính năng nghiệp vụ mới ngoài phạm vi SRS.
- **Tiêu chí nghiệm thu & Kiểm thử:**
  1. `0` tham chiếu tới `dark-compat.css` hay `poster-*.svg` giả lập trên giao diện người dùng.
  2. `100%` bài kiểm thử xUnit, `static_schema_checks.py`, `Runner verify`, `Runner race-test` và toàn bộ bộ Playwright E2E đạt **PASS**.
  3. Kiểm toán tự động trên toàn bộ trang người dùng: mọi ảnh tải thành công (`naturalWidth > 0`), `scrollWidth <= clientWidth` trên cả desktop và mobile `320px`/`390px`, tỷ lệ tương phản chữ đạt chuẩn WCAG AA, đầy đủ dòng ghi nhận nguồn `TMDB` / `IMDb` / `Creative Commons`.
- **Rủi ro & Rollback:** sót một lớp CSS phụ ở view ít dùng → Quét tĩnh toàn bộ `.cshtml` tìm các lớp `btn-`, `bg-`, `table-`, `badge`, `alert-`, `data-bs-` trước khi xóa thư viện `bootstrap`.

---

## Phần 6. Đối chiếu Tiêu chí Nghiệm thu Kế hoạch (Mục N) & Prompt Triển khai Giai đoạn 1

### 6.1. Bảng Trả lời Trực tiếp 9 Câu hỏi Nghiệm thu Kế hoạch

| Câu hỏi nghiệm thu | Câu trả lời xác quyết trong Kế hoạch |
| :--- | :--- |
| **1. Giao diện sẽ giống Netflix ở những đặc điểm cụ thể nào?** | Header chuyển mượt từ lớp phủ trong suốt trên ảnh nền sang nền kính mờ `#0A0A0C` kèm ô tìm kiếm mở rộng trực tiếp; Hero toàn chiều ngang với vùng chữ an toàn 42% bên trái và hai lớp vignette; hàng phim đầu tiên kéo chồng nhẹ lên chân Hero; kết hợp thẻ cảnh phim ngang `16:9` và poster dọc `2:3`; thẻ Hover Quick-Preview nổi có độ trễ `320ms`; hộp thoại xem Trailer chính thức; nền tối đa lớp (`#0A0A0C` → `#16161B` → `#1E1E25`) và phông tiếng Việt sắc nét `Be Vietnam Pro`. |
| **2. Những phần nào được điều chỉnh để phục vụ đặt vé tại rạp?** | Không giả làm ứng dụng xem phim trực tuyến hay trang bán gói thuê bao: CTA chính luôn dẫn tới **Chọn suất chiếu (`UTC+7`) / Chọn ghế**; thẻ phim mở bán hôm nay gắn trực tiếp các chip giờ chiếu; tách rõ thông tin tác phẩm và khả năng đặt vé thực tế tại rạp; sơ đồ ghế ưu tiên đa dấu hiệu nhận biết & thao tác bàn phím chính xác; vùng mã QR trên vé điện tử luôn khóa nền trắng tinh `#FFFFFF`. |
| **3. Mọi dữ liệu phim hiển thị đến từ đâu?** | Từ các nguồn xác minh được đối chiếu theo khóa định danh ngoài (`wikidata_qid`, `imdb_id`, `tmdb_id`): **TMDB API v3** (khi cấu hình `BGP_TMDB_READ_TOKEN`), **Wikidata + Wikimedia Commons** (metadata mở & artwork có giấy phép rõ ràng), **IMDb Non-Commercial Datasets (`title.ratings.tsv.gz`)** cho điểm số/lượt bình chọn và **kênh YouTube chính thức** cho trailer. Dữ liệu suất chiếu/phòng/giá do hệ thống rạp quản lý riêng và gắn nhãn mô phỏng học thuật. |
| **4. Điểm đánh giá có đúng nguồn, đúng thang và đủ ngữ cảnh không?** | Có. Luôn hiển thị đầy đủ `[Tên nguồn] [Điểm]/[Thang gốc] ([Số lượt đánh giá])` kèm thời điểm trích xuất (ví dụ `IMDb 7.8/10 • 95.223 lượt • 03/11/2026`). Tuyệt đối không đổi tên điểm TMDB thành IMDb, không tự nhân phần trăm hay tạo nhãn `"98% phù hợp"`. |
| **5. Artwork có thật và được phép sử dụng theo cách đề xuất không?** | Có. Trong bộ Prototype tĩnh lưu trong kho mã, chỉ sử dụng 20 file ảnh thật thuộc **`Public Domain` / `CC0` / `CC BY 3.0` / `CC BY 4.0`** đã kiểm tra từng trang giấy phép trên Wikimedia Commons và ghi đầy đủ tác giả trong [`ASSET_MANIFEST.json`](file:///d:/Projects/BetaGiaiPhong/docs/ui/prototype/assets/ASSET_MANIFEST.json). Hai bộ phim Việt thương mại năm 2024 (*Mai*, *Đào, phở và piano*) được đánh dấu rõ `PENDING_TMDB_API_KEY` và chưa lưu ảnh poster thương mại khi chưa kết nối khóa TMDB. |
| **6. Thiếu dữ liệu thì UI xử lý thế nào?** | Ẩn khối phụ không có dữ liệu (như nút trailer hoặc điểm đánh giá khi chưa có lượt chấm), ghi `"Chưa có phân loại tại VN"` thay vì tự quy đổi nhãn Mỹ sang `T13`/`T18`, dùng khung trung tính **`Chưa có hình ảnh`** khi thiếu/lỗi poster và tự động loại phim thiếu backdrop/synopsis khỏi vị trí Hero trang chủ. |
| **7. Frontend hiện tại có đáp ứng được không, nếu đổi thì chuyển đổi thế nào?** | Giữ nền tảng **ASP.NET Core 8 MVC/Razor SSR** để bảo toàn hiệu năng LCP, Cookie Auth, CSRF và 182 bài test tự động, **nhưng loại bỏ Bootstrap khỏi tầng thị giác** bằng hệ CSS độc lập `bgp-*` + Vanilla ES Modules & JSON Islands, chuyển đổi cuốn chiếu theo từng nhóm màn hình qua hai layout mới (`_CinemaLayout` & `_OperationsLayout`). |
| **8. Các luồng đang hoạt động được bảo toàn ra sao?** | Giữ nguyên 100% URL, HTTP verbs, tên trường form, Cookie `.BetaGiaiPhong.Auth`, `X-XSRF-TOKEN`, `returnUrl`, khóa giao dịch đặt ghế và kiểm tra toàn vẹn `Runner verify` xuyên suốt cả 8 giai đoạn; các màn hình chưa tới lượt nâng cấp tiếp tục dùng layout cũ cho đến khi được thay thế xong ở Giai đoạn 7–8. |
| **9. Giai đoạn đầu tiên làm gì và nghiệm thu bằng gì?** | Xây dựng nền tảng Design System CSS độc lập (`tokens`, `base`, `shell-cinema`, `shell-ops`, `components`), nhúng phông chữ tiếng Việt cục bộ và triển khai hai khung điều hướng `_CinemaLayout` / `_OperationsLayout` (kèm ô tìm kiếm trực tiếp trên header & tách menu khách hàng khỏi menu nội bộ), nghiệm thu bằng 166 test xUnit, toàn bộ bộ Playwright E2E + kịch bản kiểm thử điều hướng hai khung trên desktop & mobile. |

---

### 6.2. Prompt Triển khai Giai đoạn 1 (Sẵn sàng Sử dụng Ngay cho Lượt Tiếp theo)

```markdown
# TRIỂN KHAI UI-P1 — NETFLIX-CINEMA DESIGN SYSTEM FOUNDATION & DUAL-SHELL ARCHITECTURE

Bạn đang triển khai Giai đoạn 1 (`UI-P1`) trong kế hoạch thiết kế lại giao diện hệ thống Beta Giải Phóng Cinema Ticketing System.

Repository:
https://github.com/cakhiempn-a11y/beta-giai-phong-ticketing-system
Workspace:
D:\Projects\BetaGiaiPhong

## 1. Đọc tài liệu kiến trúc và kiểm tra Baseline trước khi sửa mã
1. Đọc đầy đủ:
   - `docs/ui/FRONTEND_ARCHITECTURE_DECISION.md`
   - `docs/ui/NETFLIX_UI_MASTER_PLAN.md` (đặc biệt Phần 3 và Giai đoạn 1 ở Phần 5)
   - `docs/ui/REAL_MOVIE_CONTENT_PLAN.md`
   - Tham khảo CSS mẫu đã kiểm chứng tại `docs/ui/prototype/netflix-cinema.css`
2. Kiểm tra `git status`, `git branch`, `HEAD` và kết quả CI mới nhất trên `main`.
3. Giữ nguyên mọi tài liệu/thay đổi chưa commit hợp lệ nếu có; không `git reset --hard`, không `force-push`, không tự `push` hoặc tạo PR khi chưa được yêu cầu.
4. Tạo nhánh làm việc riêng từ `main`: `ui/p1-cinema-design-system-dual-shell`.

## 2. Phạm vi Giai đoạn 1 (In-Scope & Out-of-Scope)
### Thuộc phạm vi:
1. **Hệ thống Phông chữ Tiếng Việt Cục bộ (`wwwroot/fonts/`):**
   - Bổ sung phông chữ mở `Be Vietnam Pro` (400, 500, 600, 700, 800 — subset `vietnamese` + `latin`, định dạng `.woff2`, giấy phép `OFL-1.1`) và `JetBrains Mono` (500, 700) kèm khai báo `@font-face` `font-display: swap` để hiển thị tiếng Việt sắc nét không phụ thuộc CDN ngoài khi chạy E2E/offline.
2. **Kiến trúc CSS Thiết kế Điện ảnh Độc lập (`wwwroot/css/cinema/`):**
   - `01-tokens.css`: Đầy đủ semantic color tokens (layered obsidian `#0A0A0C` -> `#16161B` -> `#1E1E25`, brand red `#E50914`, gold `#F5C518`, status tokens đạt chuẩn tương phản WCAG AA, type scale, spacing, radii, shadows, motion easing).
   - `02-base.css`: Box-sizing, typography mặc định, `:focus-visible` vòng tương phản kép, hỗ trợ `@media (prefers-reduced-motion: reduce)`.
   - `03-shell-cinema.css`: Khung giao diện khách hàng với Header cố định `68px` chuyển mượt từ lớp phủ trong suốt sang nền kính mờ `#0A0A0C` khi cuộn (`scrollY > 32px`), ô tìm kiếm phim tích hợp ngay trên header (`GET /movies?q=...`), nhãn thông báo học thuật tinh tế, menu người dùng và ngăn kéo điều hướng mobile có vùng chạm `>= 44x44px`.
   - `03-shell-ops.css`: Khung giao diện vận hành mật độ cao (Sidebar trái trên desktop + Topbar ca trực/vai trò + Drawer gọn trên mobile) dành riêng cho `STAFF`, `MANAGER`, `ADMIN`.
   - `04-components.css`: Các nguyên tử dùng chung tiền tố `.bgp-*` (`bgp-btn`, `bgp-badge`, `bgp-input`, `bgp-card`, `bgp-dialog`, `bgp-table`, `bgp-skeleton`, `bgp-empty-state`, `bgp-toast`).
3. **Kiến trúc Hai Layout Tách biệt & Chuyển đổi An toàn Không Vỡ Trang Cũ:**
   - Tạo `Views/Shared/_CinemaLayout.cshtml` (dành cho luồng khách hàng) và `Views/Shared/_OperationsLayout.cshtml` (dành cho `/staff/*` và `/management/*`), kèm module `wwwroot/js/modules/shell-nav.js`.
   - Tách menu điều hướng: `_CinemaLayout` chỉ hiển thị các mục trải nghiệm khách hàng (`Trang chủ`, `Danh mục phim`, `Đơn hàng của tôi` khi là `CUSTOMER`) và nếu người đăng nhập có quyền `STAFF`/`MANAGER`/`ADMIN` thì hiển thị đúng 1 nút chuyển ngữ cảnh gọn gàng (`"Console Vận hành ->"`), không trộn hàng loạt liên kết nội bộ vào thanh menu khách.
   - Cơ chế tương thích giai đoạn chuyển tiếp: Cho phép các view đã nâng cấp tắt hẳn Bootstrap (`ViewData["UseLegacyBootstrap"] = false`), trong khi các view chưa nâng cấp ở Giai đoạn 3–7 vẫn nạp CSS tương thích để không phát sinh bất kỳ lỗi vỡ giao diện nào.
   - Chuyển đổi hoàn chỉnh sang `_CinemaLayout` (không Bootstrap) cho 3 trang khung tĩnh đầu tiên: `Views/Home/Privacy.cshtml` (Giới thiệu & Quy chế học thuật + Attribution nguồn dữ liệu), `Views/Account/AccessDenied.cshtml` / `Views/Home/AccessDenied.cshtml` và `Views/Shared/Error.cshtml`, đồng thời áp dụng thử nghiệm khung header mới một cách tương thích cho ứng dụng.

### Không thuộc phạm vi (Để dành đúng các giai đoạn sau):
- Không thay đổi schema cơ sở dữ liệu, migration, khóa AES/HMAC hay logic nghiệp vụ backend.
- Chưa viết lại nội dung bên trong Trang chủ, Danh mục, Sơ đồ ghế, Checkout, Vé QR hay các bảng Quản trị (sẽ lần lượt thực hiện ở Giai đoạn 2 -> 7).

## 3. Kiểm thử & Nghiệm thu Bắt buộc
1. Chạy `python scripts/static_schema_checks.py` -> PASS.
2. Chạy `dotnet build` và `dotnet test` -> toàn bộ 166 bài test xUnit PASS.
3. Chạy `Runner verify` trên DB kiểm thử tạm thời (tuyệt đối không đụng DB dữ liệu thật) -> `0 violation(s)`.
4. Cập nhật / bổ sung kịch bản Playwright E2E kiểm chứng:
   - Header chuyển trạng thái khi cuộn, tìm kiếm từ header dẫn đúng tới `/movies?q=...` và giữ từ khóa.
   - Phân tách điều hướng theo vai trò (`Guest`, `CUSTOMER`, `STAFF`, `MANAGER`, `ADMIN`).
   - Không phát sinh cuộn ngang (`scrollWidth <= clientWidth`) ở `320px`, `390px`, `768px` và `1440px`; kiểm tra `:focus-visible` bằng bàn phím.
   - Toàn bộ các kịch bản E2E hiện có (`01` -> `08`) tiếp tục PASS.
5. Chụp bộ ảnh nghiệm thu thực tế lưu tại `docs/ui/screenshots/ui-p1/` và tự đọc lại từng ảnh để xác nhận chất lượng thị giác trước khi báo cáo bàn giao tại `docs/ui/UI_P1_IMPLEMENTATION_REPORT.md`.
```
