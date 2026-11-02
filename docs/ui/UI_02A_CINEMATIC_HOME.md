# BÁO CÁO BÀN GIAO TRANG CHỦ ĐIỆN ẢNH & ARTWORK PHIM (UI-02A)
## `UI_02A_CINEMATIC_HOME.md`

**Dự án:** Hệ thống Quản lý Bán vé Trực tuyến tại Rạp chiếu phim Beta Giải Phóng  
**Giai đoạn:** UI-02A — Cinematic Homepage & Movie Artwork Discovery  
**Nhánh thực hiện:** `ui/ui-02a-cinematic-home`  
**Baseline commit:** `e1337f5e5ebbf9f4f0ef7bba516a9c631084b7e5` (Merge PR #10 — UI-01 & UI-01A, GitHub Actions run 41383285092 SUCCESS)  
**Ngày hoàn thành:** 2026-11-02  

---

## 1. Thông tin Baseline & Nhánh Thực Hiện

- **Baseline commit tham chiếu:** `e1337f5e5ebbf9f4f0ef7bba516a9c631084b7e5` (PR #10 được merge thành công vào `main` với trạng thái CI toàn bộ xanh).
- **Nhánh công việc:** `ui/ui-02a-cinematic-home` được tạo từ baseline `main` trên, giữ nguyên lịch sử commit và không đè bất kỳ thay đổi nào.
- **Nguyên tắc kỹ thuật tuân thủ nghiêm ngặt:**
  - Không tự push, không tạo PR, không merge, không tag hoặc release.
  - Không thay đổi secret, chuỗi cấu hình, khóa mã hóa AES-256 (`BGP_QR_AES_KEY_B64`), khóa ký HMAC (`BGP_MOCK_HMAC_KEY_B64`), database schema hay migration EF Core.
  - Không can thiệp nghiệp vụ đặt vé, lock ghế, thanh toán giả lập hoặc xác thực soát vé QR.
  - Giữ nguyên công nghệ cốt lõi: ASP.NET Core MVC (net10.0), Razor Views, Bootstrap 5.3.3, và JavaScript vanilla. Tuyệt đối không bổ sung framework SPA (React, Vue, Tailwind) hay thư viện carousel bên ngoài (Swiper, Slick), tận dụng CSS scroll-snap chuẩn W3C và JavaScript native.

---

## 2. Khép Lại Các Điểm Nhỏ Từ UI-01A (Mục Tiêu A – D)

Trước khi triển khai thiết kế giao diện điện ảnh mới, toàn bộ các tồn đọng kỹ thuật từ đánh giá UI-01A đã được xử lý triệt để:

### 2.1. Mục tiêu A — Khắc phục xung đột Badge sáng trong Card (`.badge.bg-light`)
- **Vấn đề tồn đọng:** Quy tắc chung `.card .bg-light:not(...)` trong `dark-compat.css` có nguy cơ ghi đè màu nền và màu chữ của badge sáng khi badge nằm bên trong thẻ (`.card`), gây suy giảm tương phản chữ.
- **Giải pháp xử lý:**
  1. Thêm `:not(.badge)` vào bộ chọn `.card .bg-light` trong `src/BetaGiaiPhong.Web/wwwroot/css/dark-compat.css`.
  2. Khai báo quy tắc cụ thể cho badge sáng nằm trong card:
     ```css
     body.bgp-app-shell .card .badge.bg-light,
     body.bgp-app-shell .card .badge.bg-light.text-dark {
         background-color: #e5e5e1 !important;
         color: #141414 !important;
         font-weight: 600;
     }
     ```
  3. Kiểm chứng thực tế computed style trong trình duyệt: Foreground `#141414` trên Background `#e5e5e1` đạt tỷ lệ tương phản **`12.87:1`** (vượt xa chuẩn WCAG AAA $\ge 7:1$).

### 2.2. Mục tiêu B — Kiểm thử Tương phản Nâng cao (Assert Tồn Tại & Toán học WCAG)
- **Vấn đề tồn đọng:** Kiểm thử trước đây chỉ kiểm tra chuỗi RGB cơ bản và dùng điều kiện `if` khiến một số phần tử có thể bị bỏ qua âm thầm; chưa đo tỷ lệ tương phản thực tế dựa trên độ chói tương đối (relative luminance); chưa kiểm tra nút outline thực thụ.
- **Giải pháp xử lý:**
  1. Cập nhật bài kiểm tra 8.3 trong `e2e/tests/04-responsive-and-a11y.spec.ts`:
     - Assert phần tử bắt buộc tồn tại: `expect(await el.count()).toBeGreaterThan(0)` (không dùng `if` âm thầm bỏ qua).
     - Triển khai hàm tính toán độ chói tương đối $L = 0.2126 R + 0.7152 G + 0.0722 B$ (theo công thức chuẩn WCAG 2.1) và tính tỷ lệ $(L_1 + 0.05) / (L_2 + 0.05)$.
     - Hỗ trợ phân tích nền phân lớp (nếu nền trong suốt `rgba(0,0,0,0)`, tự động truy vết nền cha hoặc gán nền surface `#1F1F1F`).
     - Đo trực tiếp badge sáng (`.badge.bg-warning`, `.badge.bg-info`, `.badge.bg-light` cả ngoài và trong card) $\ge 7.0:1$.
     - Đo trực tiếp nút outline thực thụ (`.btn-outline-primary`: chữ trắng `#F5F5F1`, viền đỏ `#E50914`, tương phản $\ge 4.5:1$ trên surface).
     - Đo trực tiếp viền ô nhập liệu (`input.form-control`, `select.form-select`: viền `#767676` trên nền `#212121`, tương phản $\ge 3.0:1$ theo WCAG 1.4.11 non-text contrast).

### 2.3. Mục tiêu C — Bằng chứng Xóa Database & Hoàn Thiện Công Cụ Chụp Ảnh
- **Vấn đề tồn đọng:** Kiểm thử teardown chưa trực tiếp truy vấn SQL Server để chứng minh database tạm thời (`BetaGiaiPhong_E2E_*`) đã biến mất hoàn toàn; công cụ capture chưa báo lỗi rõ ràng nếu bước seed tài khoản quản trị thất bại.
- **Giải pháp xử lý:**
  1. Cập nhật `e2e/tests/06-capture-lifecycle.spec.ts`:
     - Sau khi quy trình teardown hoàn tất, kiểm tra thực thi lệnh SQL Server trực tiếp qua Runner: `runRunnerCommand(['e2e-sql', 'check-db-exists'], state)`.
     - Xác nhận kết quả trả về `DB_EXISTS:0` và xác nhận kết nối trực tiếp vào database tạm thời bị từ chối chủ động (`Cannot open database "..." requested by the login`).
  2. Nâng cấp công cụ `e2e/tools/capture-ui.ts`:
     - Phân tích chuỗi kết nối từ nguồn tin cậy (`masterConnection`, fallback regex an toàn).
     - Kiểm tra kết quả tạo/seed tài khoản quản trị viên: nếu thất bại, ném lỗi rõ ràng kèm mã lỗi chi tiết và log đầy đủ (không nuốt lỗi).
     - Bổ sung cơ chế thử lại retry cho các thao tác SQL (`executeSqlInDb` thử tối đa 5 lần với quãng nghỉ 600ms) để xử lý ổn định độ trễ kết nối tạm thời của SQL Server khi chuyển đổi ngữ cảnh.

### 2.4. Mục tiêu D — Tính Khách Quan & Trung Thực Trong Tài Liệu
- Làm rõ trong toàn bộ tài liệu: Mức phóng to 175% được kiểm thử thông qua mô phỏng thuộc tính CSS `document.body.style.zoom = '1.75'`, không phải zoom native của browser rendering engine.
- Báo cáo số liệu kiểm thử chính xác tuyệt đối, không làm tròn hoặc dùng cụm từ cảm tính như "100% resolved" khi chưa có kiểm chứng thực nghiệm.

---

## 3. Quyết Định Thiết Kế Trang Chủ Điện Ảnh (Cinematic Homepage)

### 3.1. Triết lý Thiết kế: Lấy Cảm Hứng Netflix, Giữ Bản Chất Đặt Vé Rạp
- **Trải nghiệm khám phá điện ảnh (Cinematic Discovery):** Trang chủ không còn là một bảng danh sách khô cứng. Ngay khi bước vào, người dùng được chào đón bằng một không gian đậm chất điện ảnh với phim nổi bật kích thước lớn, hình ảnh sống động và không khí rạp phim sang trọng.
- **Tập trung vào hành động đặt vé:** Mặc dù lấy cảm hứng thị giác từ Netflix (màu tối `#141414`, đỏ hành động `#E50914`, typography phân cấp, card phim tỷ lệ 2:3), toàn bộ chức năng đều hướng tới dịch vụ rạp chiếu: xem lịch chiếu, chọn suất và đặt ghế tại Beta Giải Phóng. Tuyệt đối không thêm nút xem phim online hay tính năng stream video giả.

### 3.2. Cinematic Hero Lớn (520–640px)
- **Kích thước & Bố cục:** Chiều cao từ 520px đến 640px trên màn hình desktop, chuyển đổi linh hoạt dạng khối dọc trên tablet và mobile.
- **Phông nền sân khấu (Backdrop):** Sử dụng asset vector `hero-cinema-backdrop.svg` kết hợp lớp phủ gradient đa tầng:
  `linear-gradient(to top, #141414 0%, rgba(20,20,20,0.85) 45%, rgba(20,20,20,0.2) 100%)` cùng gradient ngang từ trái sang phải, tạo chiều sâu thị giác và làm nổi bật phần chữ tiêu đề.
- **Thứ bậc thông tin rõ ràng:**
  - Tiêu đề phim lớn (`h1.bgp-hero-title`, 2.5rem – 3.25rem), hỗ trợ ngắt dòng và co giãn an toàn khi tiêu đề dài.
  - Huy hiệu phân loại độ tuổi chuẩn điện ảnh: `.bgp-age-badge` (viền đỏ nổi bật, chữ trắng sáng, hỗ trợ các nhãn `P`, `K`, `T13`, `T16`, `T18`).
  - Siêu dữ liệu phụ: Thời lượng phim (phút) và thể loại, phân tách bởi dấu chấm trung tâm (`•`).
  - Tóm tắt phim (`Synopsis`): Giới hạn tối đa 3 dòng hiển thị (`-webkit-line-clamp: 3`) để đảm bảo không vỡ khung hình.
- **Hệ thống Nút Kép (Dual CTA):**
  - **Nút hành động chính (Primary CTA):** Nút đỏ Netflix `#E50914` với nhãn *"Xem lịch chiếu"*, có biểu tượng vé xem phim, nhấp vào sẽ cuộn mượt xuống khu vực lịch chiếu sắp tới (`#upcoming-showtimes`).
  - **Nút hành động phụ (Secondary CTA):** Nút viền outline `#F5F5F1` với nhãn *"Khám phá phim"*, cuộn xuống hàng phim đang chiếu (`#now-showing`).
- **Poster Hero:** Khung hiển thị poster dọc tỷ lệ chuẩn 2:3 với bóng đổ nổi `box-shadow: 0 12px 32px rgba(0,0,0,0.6)`, sử dụng `loading="eager"` để tối ưu hóa chỉ số LCP (Largest Contentful Paint).
- **Quy tắc chọn Hero xác định (Deterministic Rule):**
  1. Phim đầu tiên đã phát hành (`PUBLISHED`) có suất chiếu khả dụng (`UpcomingShowtimesCount > 0`).
  2. Nếu không có suất chiếu nào, chọn phim đã phát hành đầu tiên theo thứ tự xuất bản.
  3. Nếu không có phim nào trong hệ thống, hiển thị banner giới thiệu không gian rạp chiếu phim Beta Giải Phóng thân thiện kèm lời mời đón chờ lịch chiếu.

### 3.3. Hàng Phim Cuộn Ngang (Movie Rail)
- **Cơ chế cuộn:** Sử dụng CSS Scroll-Snap chuẩn W3C (`scroll-snap-type: x mandatory; scroll-behavior: smooth`) kết hợp JavaScript native gọn nhẹ (`home-cinema.js`). Không dùng thư viện ngoài nặng nề.
- **Thẻ phim (Movie Card):**
  - Tỷ lệ khung hình poster chuẩn điện ảnh **2:3** (`aspect-ratio: 2 / 3`).
  - Khung thẻ bo góc `8px`, nền tối surface `#1F1F1F`, viền mỏng `#333333`.
  - Hiệu ứng tương tác: Khi hover, thẻ hơi phóng to nhẹ (`transform: scale(1.03)`), bóng đổ sâu hơn và viền chuyển sang màu đỏ thương hiệu.
  - Thông tin gắn kèm: Badge độ tuổi nằm nổi ở góc trên poster; nhãn thời lượng và thể loại hiển thị gọn bên dưới poster.
  - Trợ năng & Phím bấm: Toàn bộ thẻ card là một thẻ `<a>` duy nhất dẫn đến trang chi tiết `/movies/{id}`, không chứa thẻ `<button>` con nào bên trong (tránh lỗi nested interactive elements). Hỗ trợ focus phím `Tab` với đường viền focus hai lớp sắc nét.
- **Nút điều hướng Cuộn (Rail Navigation Controls):**
  - Nút mũi tên Trái/Phải nằm ở góc trên bên phải của tiêu đề hàng phim.
  - Kích thước đạt chuẩn touch-target tối thiểu của WCAG: $44 \times 44\text{px}$.
  - Trạng thái trực quan: Nút tự động mờ (`opacity: 0.35`) và vô hiệu hóa (`pointer-events: none`, `aria-disabled="true"`) khi cuộn đến điểm đầu hoặc điểm cuối của danh sách.
- **Trạng thái rỗng (Empty State):** Khi danh sách phim trống, hiển thị card thông báo nền tối tinh tế với biểu tượng rạp phim, thông điệp hướng dẫn rõ ràng và nút làm mới lịch chiếu.

### 3.4. Danh Sách Suất Chiếu Sắp Tới (Upcoming Showtimes)
- **Bề mặt hiển thị:** Nền tối `#1F1F1F` với viền `#333333`, bo góc mềm mại.
- **Bảng dữ liệu tương phản cao:**
  - Bảng responsive hỗ trợ cuộn ngang an toàn trên thiết bị màn hình nhỏ.
  - Cột hiển thị: Thời gian chiếu (định dạng giờ Việt Nam UTC+7 chuẩn xác `HH:mm - dd/MM/yyyy`), Tên phim kèm badge độ tuổi, Phòng chiếu, và Nút hành động.
- **Hành động đặt vé trực tiếp:** Mỗi hàng cung cấp nút *"Xem sơ đồ ghế"* dẫn thẳng đến đúng tuyến đường chọn ghế thực tế của hệ thống: `/showtimes/{id}/seats`. Tuyệt đối không tạo URL giả lập.

---

## 4. Hệ Thống Artwork & Cơ Chế Fallback An Toàn

### 4.1. Tài Nguyên Đồ Họa Tự Thiết Kế (100% Vector SVG)
Toàn bộ tài nguyên đồ họa mới được tạo ra bằng mã SVG nội bộ, tự chủ 100% bản quyền, không sử dụng hình ảnh từ Internet hay logo Netflix:

1. **`src/BetaGiaiPhong.Web/wwwroot/images/poster-fallback.svg`:**
   - Kích thước viewBox: `0 0 300 450` (chuẩn tỷ lệ dọc 2:3).
   - Thiết kế: Nền đen chuyển sắc `#121212` đến `#1e1e1e`, dải lỗ phim điện ảnh (film perforations) chạy dọc hai bên mép, huy hiệu tròn trung tâm với biểu tượng cuộn phim điện ảnh (cinema film reel), nhãn nhận diện thương hiệu `BETA CINEMA` và dòng phụ `GIẢI PHÓNG`.
   - Mục đích: Dùng làm ảnh thay thế tức thì khi phim chưa có poster hoặc URL poster bị lỗi.

2. **`src/BetaGiaiPhong.Web/wwwroot/images/hero-cinema-backdrop.svg`:**
   - Kích thước viewBox: `0 0 1920 1080`.
   - Thiết kế: Ánh đèn sân khấu điện ảnh (theatrical overhead spotlights) rọi từ trên trần rạp chiếu, hiệu ứng hạt bụi điện ảnh tinh tế, ghế rạp phim mờ ảo phía dưới và vùng tối dần chuyển thành `#141414` ở các mép.
   - Mục đích: Làm hình nền cho Cinematic Hero khi phim không có hình nền backdrop riêng.

### 4.2. Cơ Chế Fallback Chống Lặp Vô Hạn (Infinite Loop Prevention)
Trên toàn bộ thẻ `<img>` của Hero và Movie Rail, bộ xử lý lỗi JavaScript được tích hợp sẵn:
```html
<img src="@posterSrc"
     alt="Poster phim @movie.Title"
     class="bgp-card-poster"
     loading="lazy"
     onerror="this.onerror=null; this.src='/images/poster-fallback.svg';" />
```
- Thuộc tính `this.onerror=null` đảm bảo ngắt lập tức listener lỗi trước khi gán đường dẫn fallback, ngăn chặn tuyệt đối tình trạng trình duyệt gửi yêu cầu mạng lặp vô tận nếu tệp fallback gặp sự cố.

---

## 5. Phân Tách Mã Nguồn & Tương Thích Kiến Trúc

Để bảo vệ nền tảng UI-01 và đảm bảo tính mô-đun hóa cao:
1. **Kiểu dáng được cô lập (Scoped Styles):**
   - Tệp mới: `src/BetaGiaiPhong.Web/wwwroot/css/home-cinema.css`.
   - Toàn bộ bộ chọn CSS được bao bọc dưới lớp `.bgp-home`, `.bgp-home-hero`, `.bgp-movie-rail`, `.bgp-movie-card`, `.bgp-upcoming-card`.
   - Không can thiệp hoặc ghi đè kiểu dáng toàn cục ngoài phạm vi trang chủ.
   - Hỗ trợ chuẩn trợ năng chuyển động giảm thiểu (`@media (prefers-reduced-motion: reduce)`: tắt chuyển động cuộn và hiệu ứng hover phóng to).
2. **Kịch bản tương tác độc lập (Scoped Script):**
   - Tệp mới: `src/BetaGiaiPhong.Web/wwwroot/js/home-cinema.js`.
   - Tự động phát hiện DOM `.bgp-rail-track`, thiết lập listener sự kiện `scroll` có debounce, tính toán vị trí cuộn và cập nhật trạng thái `disabled` của nút bấm.
3. **Mở rộng Layout với Section Styles:**
   - Trong `src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml`, bổ sung `@await RenderSectionAsync("Styles", required: false)`.
   - Trang chủ chỉ nạp `home-cinema.css` khi hiển thị, không làm nặng các trang quản trị hay giỏ hàng khác.
4. **Ngăn chặn Chromium Caching trên Trang Chủ:**
   - Trong `src/BetaGiaiPhong.Web/Controllers/HomeController.cs`, gắn thuộc tính:
     `[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]`
   - Đảm bảo khi người dùng tạo suất chiếu mới hoặc quản trị viên cập nhật danh mục phim, trang chủ luôn hiển thị dữ liệu mới nhất mà không bị giữ lại bộ nhớ cache cũ của trình duyệt.

---

## 6. Danh Sách Tệp Thay Đổi & Tạo Mới

| STT | Tệp tin | Trạng thái | Mục đích & Chi tiết thay đổi |
|---|---|---|---|
| 1 | `src/BetaGiaiPhong.Web/Models/Responses/MovieDtos.cs` | Sửa đổi | Bổ sung các trường `AgeLabel`, `Synopsis`, `PosterUrl` vào `MovieSummaryDto` và `MovieDetailDto` (tương thích backward). |
| 2 | `src/BetaGiaiPhong.Web/Services/CatalogService.cs` | Sửa đổi | Chiếu các cột `AgeLabel`, `Synopsis`, `PosterUrl` trong các truy vấn `GetPublishedMoviesAsync` và `GetMovieDetailAsync`. |
| 3 | `src/BetaGiaiPhong.Web/Models/ViewModels/HomeViewModel.cs` | Sửa đổi | Bổ sung thuộc tính `HeroMovie` kiểu `MovieSummaryDto?` vào mô hình hiển thị trang chủ. |
| 4 | `src/BetaGiaiPhong.Web/Controllers/HomeController.cs` | Sửa đổi | Cài đặt quy tắc chọn Hero xác định (ưu tiên phim có suất chiếu, fallback phim xuất bản đầu tiên) và thêm `[ResponseCache(NoStore = true)]`. |
| 5 | `src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml` | Sửa đổi | Thêm `@await RenderSectionAsync("Styles", required: false)` để hỗ trợ nạp CSS theo từng trang. |
| 6 | `src/BetaGiaiPhong.Web/Views/Home/Index.cshtml` | Sửa đổi | Tái cấu trúc hoàn chỉnh giao diện: Cinematic Hero, Hàng phim cuộn ngang, Card 2:3, Bảng suất chiếu sắp tới, và nhúng CSS/JS scoped. |
| 7 | `src/BetaGiaiPhong.Web/wwwroot/css/dark-compat.css` | Sửa đổi | Xử lý triệt để xung đột `.badge.bg-light` bên trong `.card`, đảm bảo chữ tối `#141414` trên nền sáng `#e5e5e1`. |
| 8 | `src/BetaGiaiPhong.Web/wwwroot/css/home-cinema.css` | **Tạo mới** | Tệp CSS scoped cho toàn bộ thành phần trang chủ điện ảnh, hero, movie rail, và card phim. |
| 9 | `src/BetaGiaiPhong.Web/wwwroot/js/home-cinema.js` | **Tạo mới** | Kịch bản điều khiển cuộn hàng phim mượt mà và cập nhật trạng thái phím điều hướng. |
| 10 | `src/BetaGiaiPhong.Web/wwwroot/images/poster-fallback.svg` | **Tạo mới** | Đồ họa vector poster dự phòng chuẩn 2:3 với nhận diện Beta Cinema và cuộn phim. |
| 11 | `src/BetaGiaiPhong.Web/wwwroot/images/hero-cinema-backdrop.svg` | **Tạo mới** | Đồ họa vector hình nền rạp phim 1920x1080 với ánh đèn sân khấu. |
| 12 | `e2e/tools/capture-ui.ts` | Sửa đổi | Bổ sung kịch bản chụp màn hình UI-02A, phân tích kết nối an toàn, và cơ chế retry SQL. |
| 13 | `e2e/tests/04-responsive-and-a11y.spec.ts` | Sửa đổi | Nâng cấp test 8.3 đo tương phản toán học WCAG, assert phần tử tồn tại, và test outline buttons. |
| 14 | `e2e/tests/06-capture-lifecycle.spec.ts` | Sửa đổi | Nâng cấp kiểm chứng trực tiếp qua SQL Server xác nhận database tạm thời đã bị xóa sạch (`DB_EXISTS:0`). |
| 15 | `e2e/tests/07-cinematic-home.spec.ts` | **Tạo mới** | Bộ kiểm thử E2E Playwright chuyên sâu gồm 6 kịch bản cho toàn bộ tính năng trang chủ điện ảnh. |

---

## 7. Thư Viện Ảnh Chụp Màn Hình (`docs/ui/screenshots/ui-02a/`)

Toàn bộ 9 bức ảnh chụp màn hình thực tế trong phiên bản phát hành (Release mode) đã được kết xuất và lưu trữ tại thư mục `docs/ui/screenshots/ui-02a/`:

| Tên tệp ảnh | Kích thước | Mô tả nội dung ảnh chụp |
|---|---|---|
| `01-desktop-home-full.png` | 337,282 bytes | Trang chủ toàn cảnh trên Desktop ($1440 \times 900$), hiển thị trọn vẹn Hero lớn, Hàng phim đang chiếu với poster 2:3, và Bảng suất chiếu sắp tới. |
| `02-mobile-home-full.png` | 127,400 bytes | Trang chủ toàn cảnh trên Mobile ($390 \times 844$), bố cục co giãn mượt mà, typography rõ ràng, không có thanh cuộn ngang toàn trang. |
| `03-tablet-home.png` | 234,212 bytes | Trang chủ trên Tablet ($768 \times 1024$), bố cục 2 cột cân đối, thanh điều hướng linh hoạt và thẻ phim hiển thị tối ưu. |
| `04-hero-cinematic.png` | 210,917 bytes | Cận cảnh Cinematic Hero với nền sân khấu, badge độ tuổi `P`, tiêu đề phim, nút bấm kép và poster tải sớm `loading="eager"`. |
| `04b-hero-long-title.png` | 256,926 bytes | Cận cảnh Hero trong trường hợp tiêu đề phim dài đặc biệt; chứng minh văn bản tự động xuống dòng và co giãn an toàn, không đè nút hay vỡ khung. |
| `05-movie-rail-keyboard-focus.png` | 95,602 bytes | Cận cảnh hàng phim đang chiếu khi người dùng điều hướng bằng phím `Tab`, hiển thị chỉ báo focus hai lớp sắc nét trên thẻ phim. |
| `06-empty-state.png` | 154,801 bytes | Trạng thái rỗng (Empty state) khi hệ thống chưa có phim nào phát hành; hiển thị banner giới thiệu không gian rạp Beta và thông báo mời đón chờ lịch chiếu. |
| `07-artwork-fallback.png` | 31,275 bytes | Cận cảnh thẻ phim khi poster gốc bị lỗi tải; trình duyệt tự động kích hoạt `onerror` và hiển thị ảnh vector `poster-fallback.svg` hoàn hảo. |
| `08-upcoming-showtimes.png` | 15,719 bytes | Cận cảnh bảng danh sách suất chiếu sắp tới trên nền surface tối `#1F1F1F` với các nút *"Xem sơ đồ ghế"* dẫn trực tiếp tới `/showtimes/{id}/seats`. |

---

## 8. Kết Quả Kiểm Thử Toàn Diện (Test Execution Evidence)

### 8.1. Biên Dịch Mã Nguồn (.NET Release Build)
- **Lệnh thực hiện:** `dotnet build BetaGiaiPhong.slnx -c Release`
- **Kết quả:** **`0 Cảnh báo (Warnings), 0 Lỗi (Errors)`**. Toàn bộ 5 dự án trong solution biên dịch hoàn toàn sạch sẽ.

### 8.2. Bộ Kiểm Thử Đơn Vị & Tích Hợp (xUnit Test Suite)
- **Lệnh thực hiện:** `dotnet test BetaGiaiPhong.slnx -c Release --no-build`
- **Kết quả:** **`166 / 166 KIỂM THỬ THÀNH CÔNG (100% PASS)`**
  - `BetaGiaiPhong.Tests.CatalogTests`: Toàn bộ test truy vấn danh mục, lọc phim, bảo mật dữ liệu vượt qua.
  - `BetaGiaiPhong.Tests.OrderTests`: Toàn bộ test quy trình giữ ghế, thanh toán và hủy đơn vượt qua.
  - `BetaGiaiPhong.Tests.CheckInTests`: Toàn bộ test mã hóa QR, giải mã AES và kiểm tra vé vượt qua.

### 8.3. Bộ Kiểm Thử Trình Duyệt Thực Tế (Playwright E2E Suite)
Bộ kiểm thử chạy tuần tự trên cơ sở dữ liệu tạm thời cô lập, bao gồm 7 tệp spec với tổng cộng **15 kịch bản kiểm thử E2E**:

| Tệp Kiểm thử | Kịch bản kiểm tra | Kết quả | Thời gian chạy |
|---|---|---|---|
| `01-full-journey.spec.ts` | Toàn bộ vòng đời: Quản lý tạo lịch -> Khách đặt vé -> Nhân viên soát vé QR -> Báo cáo doanh thu | **PASS** | 21.9s |
| `02-negative-and-security.spec.ts` | Kịch bản tiêu cực: Trùng lịch phòng chiếu, hết hạn giữ chỗ, chặn truy cập trái quyền | **PASS** | 21.4s |
| `03-restart-recovery.spec.ts` | Khởi động lại máy chủ Web, tính lũy kế idempotent của Webhook, phục hồi kết nối | **PASS** | 52.5s |
| `04-responsive-and-a11y.spec.ts` | Layout đa màn hình (Desktop/Tablet/Mobile), đo độ tương phản toán học WCAG 8.3, tràn khung 320px 8.4, menu mobile 8.5 | **PASS** | 19.4s |
| `05-performance-load.spec.ts` | Tải đồng thời 30 yêu cầu đặt vé, kiểm tra P95 latency và thông lượng | **PASS** | 9.7s |
| `06-capture-lifecycle.spec.ts` | Vòng đời công cụ capture, đảm bảo drop database tạm thời sạch sẽ và ngắt tiến trình | **PASS** | 15.3s |
| `07-cinematic-home.spec.ts` | 6 kịch bản chuyên sâu trang chủ điện ảnh (Hero, Movie Rail, Fallback Artwork, Showtimes Table, Responsive 320-1440px, Navigation Integrity) | **PASS** | 29.0s |
| **TỔNG CỘNG** | **15 / 15 Kịch bản Kiểm thử End-to-End** | **100% PASS** | **~3.0 phút** |

---

## 9. Ranh Giới Đã Kiểm Chứng vs Chưa Kiểm Chứng

### Những điểm ĐÃ kiểm chứng chắc chắn:
1. Giao diện trang chủ (`/`) đã được nâng cấp hoàn toàn theo phong cách điện ảnh lấy cảm hứng từ Netflix, hoạt động mượt mà, đúng cấu trúc phân cấp thị giác.
2. Quy tắc chọn Hero xác định hoạt động chuẩn xác trong cả 3 tình huống: có suất chiếu, chỉ có phim, và khi rỗng.
3. Hàng phim cuộn ngang tương thích mượt mà trên chuột máy tính, cảm ứng vuốt trên mobile, và phím bấm `Tab` / nút điều hướng trợ năng.
4. Cơ chế fallback ảnh vector `poster-fallback.svg` xử lý êm dịu lỗi ảnh và không gây lặp vô hạn mạng.
5. Nút xem ghế dẫn đúng tới tuyến đường backend thật `/showtimes/{id}/seats`.
6. Toàn bộ các tồn đọng của UI-01A (tương phản badge light trong card, đo tương phản toán học, chứng minh drop DB) đã được đóng kín và kiểm chứng tự động.

### Những điểm CHƯA kiểm chứng / Nằm ngoài phạm vi:
1. **Trang danh mục phim đầy đủ (`/movies`) và Trang chi tiết phim (`/movies/{id}`):** Hiện tại vẫn giữ nguyên bố cục của UI-01/UI-01A, chưa được thiết kế lại theo phong cách điện ảnh (thuộc phạm vi UI-02B).
2. **Sơ đồ chọn ghế (`/showtimes/{id}/seats`) và Trang thanh toán (`/orders/{id}`):** Chưa thiết kế lại, chỉ giữ tính tương thích trên nền tối.
3. **Môi trường thực tế với hàng nghìn phim:** Chưa kiểm thử hiệu năng hiển thị danh mục phim vượt quá 100 tác phẩm (hiện tại trang chủ chỉ giới hạn hàng phim ở mức tối đa 12 phim đang chiếu để tối ưu hiệu năng).

---

## 10. Đề Xuất Cho Giai Đoạn Tiếp Theo (UI-02B)

Từ các nền tảng vững chắc đã thiết lập trong UI-02A, đề xuất kế hoạch triển khai cho giai đoạn UI-02B:
1. **Trang Chi Tiết Phim Điện Ảnh (`/movies/{id}`):**
   - Thiết kế lại phần đầu trang chi tiết phim với Hero Backdrop mờ (ambient blurred backdrop) tạo chiều sâu.
   - Trình bày thông tin đầy đủ: Poster lớn, độ tuổi, đạo diễn, diễn viên, ngày khởi chiếu, nội dung tóm tắt chi tiết.
   - Bộ chọn ngày chiếu trực quan dạng thanh cuộn ngang (tabs theo ngày: Hôm nay, Ngày mai, Các ngày tiếp theo) để khách hàng chọn suất chiếu thuận tiện nhất.
2. **Trang Danh Mục Phim Toàn Diện (`/movies`):**
   - Hỗ trợ xem dạng lưới (Grid View) với bộ lọc thông minh: Thể loại, Độ tuổi, Trạng thái chiếu (Đang chiếu / Sắp chiếu).
   - Duy trì tỷ lệ poster 2:3 đồng bộ với trang chủ.
3. **Tiếp tục hoàn thiện các thành phần tương tác:**
   - Đảm bảo tính liên kết xuyên suốt từ Trang chủ $\rightarrow$ Chi tiết phim $\rightarrow$ Chọn suất chiếu $\rightarrow$ Chọn ghế.
