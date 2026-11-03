# UI-02B — Cinematic Movie Catalogue & Movie Details

## 1. Mục tiêu và Phạm vi UI-02B

Giai đoạn **UI-02B** hoàn thiện hành trình khám phá phim và chọn suất chiếu của khách hàng trên nền tảng thiết kế tối lấy cảm hứng từ Netflix:
- **Hành trình người dùng**: Trang chủ (`/`) → Danh mục phim (`/movies`) → Chi tiết phim (`/movies/{id}`) → Chọn suất chiếu theo ngày (`UTC+7`) → Trang sơ đồ ghế hiện có (`/showtimes/{id}/seats`).
- **Phạm vi**:
  - Thiết kế lại hoàn toàn trang danh mục phim `/movies` và trang chi tiết phim `/movies/{id}`.
  - Bổ sung cơ chế tìm kiếm và lọc phim qua URL query (`GET /movies?q=...&filter=...`) giữ nguyên trạng thái khi tải lại trang, chia sẻ liên kết hoặc bấm nút Back của trình duyệt.
  - Chuẩn hóa việc gom nhóm và sắp xếp suất chiếu theo ngày giờ Việt Nam (`UTC+7`) dựa trên kiểu dữ liệu thời gian `DateTimeOffset` từ `StartUtc` và `EndUtc`, có hỗ trợ bộ chọn ngày tương tác (Progressive Enhancement) và hiển thị rõ các suất chiếu qua nửa đêm (`+1 ngày`) hoặc đã hết ghế trống.
  - Khép lại hai vấn đề tồn đọng về fixture E2E từ giai đoạn trước: khôi phục dữ liệu bằng bảng snapshot theo `movie_id` và kiểm tra nghiêm ngặt mã thoát (`status !== 0`) của mọi lệnh runner.
  - Không thay đổi giao diện sơ đồ ghế, thanh toán, tài khoản, quét vé hoặc quản trị trong giai đoạn này.

---

## 2. Xác nhận Trạng thái Baseline & CI

Trước khi triển khai trên nhánh `ui/ui-02b-movie-discovery` (tạo từ `main` tại commit `df197b9cf298774512cd5af8fc5e764fd31ae204`), hai lượt chạy GitHub Actions CI của baseline đã được xác minh trực tiếp:

1. **CI Run `41563087699`** (PR #11 — commit `9ed07435a3d0a79f8d7d5f8238006f0b58fff94f`):
   - `Build & Static Validation`: **SUCCESS**
   - `Automated Tests with SQL Server`: **SUCCESS** (166 passed)
   - `Integrity Checks & Runner Validation`: **SUCCESS**
   - `Real-Browser E2E Gate (Playwright Chromium)`: **SUCCESS** (16 passed)
2. **CI Run `41564231737`** (Post-merge `main` — commit `df197b9cf298774512cd5af8fc5e764fd31ae204`):
   - `Build & Static Validation`: **SUCCESS**
   - `Automated Tests with SQL Server`: **SUCCESS** (166 passed)
   - `Integrity Checks & Runner Validation`: **SUCCESS**
   - `Real-Browser E2E Gate (Playwright Chromium)`: **SUCCESS** (16 passed)

---

## 3. Khép lại Hai Điểm về Fixture & Kiểm tra Mã thoát Runner

### 3.1. Khôi phục dữ liệu bằng Snapshot theo `movie_id` (`src/BetaGiaiPhong.Runner/Program.cs`)
Thay thế hoàn toàn cách `UPDATE` hàng loạt theo trạng thái hoặc giá trị cố định bằng hai bảng snapshot tạm trong cơ sở dữ liệu E2E (`dbo.__E2eMovieSnapshots` và `dbo.__E2eShowtimeFixtures`):
- **`seed-ui-fixtures` / `reset-ui-fixtures`**:
  - Trước khi cập nhật phim mẫu có sẵn, lưu bản ghi gốc theo `movie_id` với `is_inserted_fixture = 0` trong `dbo.__E2eMovieSnapshots` (`snapshot_scope = 'ui-fixtures'`).
  - Các phim fixture chèn thêm (gồm cả 1 phim ở trạng thái `DRAFT` là `Phim Ban Nhap Chua Cong Bo` và các suất chiếu nhiều ngày của `Chien Binh Khong Gian...`) được đánh dấu `is_inserted_fixture = 1` và ghi nhận `showtime_id` trong `dbo.__E2eShowtimeFixtures`.
  - Khi chạy `reset-ui-fixtures`, chỉ xóa các suất chiếu và phim có `is_inserted_fixture = 1`, đồng thời khôi phục chính xác từng cột (`title`, `duration_min`, `genre_text`, `status`, `age_label`, `synopsis`, `poster_url`) cho các bản ghi `is_inserted_fixture = 0`.
- **`hide-all-movies` / `restore-all-movies`**:
  - Lưu danh sách `movie_id` và `status` của các phim đang `PUBLISHED` vào `dbo.__E2eMovieSnapshots` (`snapshot_scope = 'hide-all'`) trước khi chuyển sang `DRAFT`.
  - Khi `restore-all-movies`, chỉ khôi phục những `movie_id` thuộc `snapshot_scope = 'hide-all'`. Phim vốn đang ở trạng thái `DRAFT` trước thao tác (`Phim Ban Nhap Chua Cong Bo`) vẫn giữ nguyên trạng thái `DRAFT`.
- **`set-long-title` / `restore-title`**:
  - Lưu `title` gốc theo `movie_id` vào `snapshot_scope = 'long-title'` và khôi phục đúng `title` gốc của `movie_id` đó.

### 3.2. Kiểm tra mã thoát (`status !== 0`) ở mọi lệnh Runner (`e2e/e2e-env.ts`, `e2e/tools/capture-ui.ts`, `e2e/tests/07-cinematic-home.spec.ts`, `e2e/tests/08-movie-discovery.spec.ts`)
- Bổ sung `runRunnerCommandOrThrow` và `runRunnerCleanupSafe` trong `e2e/e2e-env.ts`.
- Mọi lệnh runner (`seed-admin`, `seed-ui-fixtures`, `reset-ui-fixtures`, `hide-all-movies`, `restore-all-movies`, `set-long-title`, `restore-title`, `inspect-movies-state`) đều kiểm tra `status !== 0` và ném lỗi đã lọc secret (`sanitizeTextForLog`), loại bỏ hoàn toàn các nhánh chỉ `console.warn` rồi tiếp tục chạy.
- Các bước dọn dẹp trong `finally` sử dụng `runRunnerCleanupSafe` để không che khuất lỗi gốc của test nếu cả thân test và bước cleanup đều gặp lỗi.

---

## 4. Cấu trúc Trang Danh mục Phim (`/movies`) & Cơ chế Tìm kiếm / Lọc

### 4.1. Giao diện Lưới Poster Điện ảnh (`Views/Movies/Index.cshtml`, `wwwroot/css/movie-discovery.css`)
- Loại bỏ hoàn toàn khối `.movie-poster-placeholder` cũ, thay bằng khung poster chuẩn tỷ lệ `2:3` (`aspect-ratio: 2 / 3`, `object-fit: cover`, `loading="lazy"`).
- **Responsive Grid**:
  - Desktop lớn (`>= 1200px`): 5 cột.
  - Desktop vừa (`992px–1199px`): 4 cột.
  - Tablet (`576px–991px`): 3 cột.
  - Mobile (`< 576px`, kể cả `320px` và `390px`): 2 cột cân đối, không phát sinh cuộn ngang toàn trang (`scrollWidth <= clientWidth`).
- **Thẻ phim (`.bgp-catalog-card`)**:
  - Toàn bộ thẻ là một liên kết `<a>` duy nhất dẫn tới `/movies/{id}`, không chứa `<button>` hay `<a>` lồng bên trong.
  - Hiển thị nhãn độ tuổi (`AgeLabel`) nếu có, thời lượng (`DurationMin`), thể loại (`GenreText`) và trạng thái lịch chiếu trung thực theo dữ liệu thực tế:
    - `UpcomingShowtimesCount > 0`: `"Có lịch chiếu • X suất"`
    - `UpcomingShowtimesCount == 0`: `"Chưa có lịch chiếu"`

### 4.2. Tìm kiếm và Lọc qua URL Query (`MoviesController.Index`)
- Hỗ trợ `GET /movies?q=...&filter=...` với 3 bộ lọc:
  - `all`: Tất cả phim đã công bố (`PUBLISHED`).
  - `has-showtimes`: Chỉ các phim đang có suất chiếu sắp tới (`UpcomingShowtimesCount > 0`).
  - `no-showtimes`: Các phim đã công bố nhưng chưa có suất chiếu sắp tới (`UpcomingShowtimesCount == 0`).
- Phân biệt rõ hai trạng thái rỗng:
  - **Hệ thống chưa có phim `PUBLISHED` nào** (`TotalPublishedCount == 0` — `[data-testid="catalog-empty-system"]`).
  - **Không có phim khớp từ khóa/bộ lọc** (`TotalPublishedCount > 0 && Movies.Count == 0` — `[data-testid="catalog-empty-filter"]`) kèm nút `"Xóa bộ lọc & Xem tất cả phim"`.

---

## 5. Trang Chi tiết Phim (`/movies/{id}`) & Bộ chọn Suất chiếu theo Ngày (`UTC+7`)

### 5.1. Hero Chi tiết Phim (`Views/Movies/Details.cshtml`)
- Giữ poster đứng tỷ lệ `2:3` trong khung `.bgp-detail-poster-frame`, kết hợp nền tối `/images/hero-cinema-backdrop.svg` và lớp phủ gradient (không kéo giãn poster dọc thành banner ngang).
- Hiển thị đầy đủ tên phim, nhãn độ tuổi, thời lượng, thể loại, số lượng suất chiếu sắp tới và nội dung tóm tắt (`Synopsis`) được mã hóa HTML an toàn (hoặc thông báo cập nhật nếu `Synopsis` rỗng).
- Đã gỡ bỏ hoàn toàn thông báo kỹ thuật nội bộ `BE-01 / BE-02` và thông số `Buffer dọn phòng` khỏi giao diện khách hàng.
- Khi phim có suất chiếu: nút CTA `"Chọn suất chiếu"` cuộn tới `#showtimes-section` (có `scroll-margin-top: 88px` để không bị thanh header cố định `68px` che khuất tiêu đề).
- Khi phim chưa có suất chiếu: hiển thị trạng thái `"Hiện chưa có suất chiếu sắp tới"` kèm liên kết quay lại danh mục phim, không hiển thị CTA đặt vé giả.

### 5.2. Gom nhóm Suất chiếu theo Ngày giờ Việt Nam (`UTC+7`) & Progressive Enhancement
- Trong `MoviesController.Details`, mỗi `ShowtimeDto` được chuyển đổi từ `StartUtc` và `EndUtc` sang `DateTimeOffset` múi giờ Việt Nam (`TimeSpan.FromHours(7)`), không dùng `.Split(' ')` trên chuỗi định dạng.
- Các ngày chiếu (`DateOnly`) và các suất chiếu trong từng ngày được sắp xếp tăng dần theo thời gian bắt đầu thực tế (`StartVietnam`) rồi đến `ShowtimeId`.
- **Suất chiếu qua nửa đêm (`CrossesMidnight`)**: Khi `EndVietnam.Date > StartVietnam.Date` (ví dụ `23:15 ~ 01:30`), hiển thị rõ nhãn `+1 ngày (dd/MM)` để khách hàng không nhầm lẫn ngày kết thúc.
- **Suất chiếu hết ghế trống (`AvailableSeatsCount == 0`)**: Hiển thị nhãn `"Hết ghế trống (0 / SellableCapacity)"` và nút `"Xem sơ đồ ghế"` thay vì `"Chọn ghế"`.
- **Bộ chọn ngày (`wwwroot/js/movie-discovery.js`)**:
  - Hỗ trợ chọn ngày bằng chuột và bàn phím (`Tab`, `ArrowRight`, `ArrowLeft`, `Home`, `End`, `Space`, `Enter`), đồng bộ tham số `?date=yyyy-MM-dd` qua `history.pushState` và `popstate`.
  - **Progressive Enhancement**: Khi JavaScript bị tắt, toàn bộ các nhóm ngày chiếu đều hiển thị đầy đủ trên trang và các tab ngày vẫn hoạt động qua liên kết `GET ?date=yyyy-MM-dd`.

---

## 6. Đồng bộ Cơ chế Poster Fallback

- Thống nhất sử dụng duy nhất cơ chế `onerror="this.onerror=null;this.src='/images/poster-fallback.svg';"` (kết hợp kiểm tra chuỗi rỗng ở phía Razor để gán sẵn `/images/poster-fallback.svg`) trên cả Trang chủ (`/`), Danh mục phim (`/movies`) và Chi tiết phim (`/movies/{id}`).
- Đã loại bỏ hàm `initPosterFallbacks()` trùng lặp trong `wwwroot/js/home-cinema.js`.

---

## 7. Danh sách File Thêm mới & Cập nhật

| File | Trạng thái | Mô tả |
| :--- | :--- | :--- |
| `src/BetaGiaiPhong.Runner/Program.cs` | Cập nhật | Snapshot theo `movie_id` cho `seed-ui-fixtures`, `reset-ui-fixtures`, `hide-all-movies`, `restore-all-movies`, `set-long-title`, `restore-title`, và thêm `inspect-movies-state`. |
| `src/BetaGiaiPhong.Web/Controllers/MoviesController.cs` | Cập nhật | Hỗ trợ tìm kiếm/lọc (`q`, `filter`) trên `/movies` và gom nhóm suất chiếu theo `DateTimeOffset` (`UTC+7`) trên `/movies/{id}`. |
| `src/BetaGiaiPhong.Web/Models/ViewModels/MovieCatalogViewModel.cs` | Thêm mới | ViewModel cho trang `/movies` (danh sách lọc, bộ đếm theo trạng thái, từ khóa và trạng thái bộ lọc). |
| `src/BetaGiaiPhong.Web/Models/ViewModels/MovieDetailViewModel.cs` | Cập nhật | Bổ sung `ShowtimeItemViewModel`, `ShowtimeDateGroupViewModel`, `DateGroups`, `SelectedDateKey`. |
| `src/BetaGiaiPhong.Web/Views/Movies/Index.cshtml` | Cập nhật | Giao diện lưới poster 2:3, thanh tìm kiếm, bộ lọc trạng thái suất chiếu và các trạng thái rỗng. |
| `src/BetaGiaiPhong.Web/Views/Movies/Details.cshtml` | Cập nhật | Hero chi tiết phim điện ảnh và bộ chọn suất chiếu theo ngày (`UTC+7`). |
| `src/BetaGiaiPhong.Web/wwwroot/css/movie-discovery.css` | Thêm mới | Stylesheet phạm vi hóa riêng cho `.bgp-movies-catalog` và `.bgp-movie-detail`. |
| `src/BetaGiaiPhong.Web/wwwroot/js/movie-discovery.js` | Thêm mới | Xử lý chuyển tab ngày chiếu, điều hướng bàn phím và đồng bộ URL `?date=`. |
| `src/BetaGiaiPhong.Web/wwwroot/js/home-cinema.js` | Cập nhật | Loại bỏ listener fallback ảnh trùng lặp để dùng chung cơ chế inline `onerror`. |
| `src/BetaGiaiPhong.Web/wwwroot/images/posters/*.svg` | Thêm mới | 4 poster vector tỷ lệ `2:3` (`poster-scifi.svg`, `poster-noir.svg`, `poster-horror.svg`, `poster-animation.svg`). |
| `e2e/e2e-env.ts` | Cập nhật | Bổ sung `runRunnerCommandOrThrow` và `runRunnerCleanupSafe` kiểm tra `status !== 0`. |
| `e2e/tools/capture-ui.ts` | Cập nhật | Dùng `runRunnerCommandOrThrow` và bổ sung chế độ chụp ảnh `ui-02b`. |
| `e2e/tests/07-cinematic-home.spec.ts` | Cập nhật | Dùng `runRunnerCommandOrThrow`/`runRunnerCleanupSafe` và kiểm tra bảo toàn phim `DRAFT` sau `restore-all-movies`. |
| `e2e/tests/08-movie-discovery.spec.ts` | Thêm mới | Bộ kiểm thử E2E toàn diện cho `/movies` và `/movies/{id}` (6 kịch bản lớn). |
| `docs/ui/screenshots/ui-02b/*.png` | Thêm mới | 9 ảnh chụp màn hình minh chứng cho UI-02B. |

---

## 8. Kết quả Kiểm thử Cục bộ & Ảnh chụp Màn hình

### 8.1. Kết quả Build & Test Cục bộ (Local Verification)
- **Build**: `dotnet build BetaGiaiPhong.slnx -c Release` → **PASS** (`0 Warning(s), 0 Error(s)`).
- **xUnit Tests**: `dotnet test BetaGiaiPhong.slnx -c Release --no-build -e BGP_TEST_ALLOW_DB_CREATE=true` → **PASS** (`166 passed, 0 failed`).
- **Playwright E2E Suite**: `npx playwright test --retries=0` → **PASS** (`22 passed, 0 failed`, bao gồm 16 test hiện có và 6 test mới trong `08-movie-discovery.spec.ts`; `Runner verify` đạt `0 violation(s)`).

*(Lưu ý: Các kết quả trên là kiểm chứng cục bộ trên máy phát triển; nhánh `ui/ui-02b-movie-discovery` chưa được push hoặc mở PR theo đúng quy tắc bàn giao).*

### 8.2. Danh mục Ảnh chụp Màn hình (`docs/ui/screenshots/ui-02b/`)
1. `01-catalog-desktop.png` — Trang danh mục phim `/movies` trên Desktop (`1440×900`).
2. `02-catalog-mobile.png` — Trang danh mục phim `/movies` trên Mobile (`390×844`, lưới 2 cột).
3. `03-catalog-search-filter.png` — Trạng thái tìm kiếm và lọc phim có lịch chiếu (`/movies?filter=has-showtimes&q=Chien+Binh`).
4. `04-catalog-no-results.png` — Trạng thái không có kết quả khớp bộ lọc kèm nút xóa bộ lọc.
5. `05-detail-desktop.png` — Trang chi tiết phim `/movies/{id}` có suất chiếu trên Desktop (`1440×900`), hiển thị cả suất chiếu qua nửa đêm `+1 ngày` và suất chiếu hết ghế trống.
6. `06-detail-mobile.png` — Trang chi tiết phim `/movies/{id}` trên Mobile (`390×844`).
7. `07-detail-no-showtimes.png` — Trang chi tiết phim chưa có suất chiếu sắp tới và poster fallback.
8. `08-poster-fallback-long-title.png` — Trạng thái poster fallback và tên phim rất dài được giới hạn 2 dòng trên lưới `/movies`.
9. `09-showtime-date-selector.png` — Bộ chọn ngày chiếu tương tác chuyển sang ngày thứ 2 (`05/11/2026`, hiển thị suất `00:30` giờ VN và `19:00` giờ VN).

---

## 9. Tác động tới các Giai đoạn Tiếp theo (UI-03)

- Mọi liên kết chọn suất chiếu trên `/movies/{id}` đều giữ nguyên route chuẩn `/showtimes/{showtimeId}/seats`, đảm bảo chuyển tiếp mượt mà sang trang sơ đồ ghế hiện tại và sẵn sàng cho giai đoạn **UI-03** (nâng cấp giao diện sơ đồ ghế, giỏ hàng và thanh toán).
- Các quy ước token màu sắc (`design-tokens.css`) và phạm vi CSS riêng (`movie-discovery.css`) hoàn toàn độc lập, không gây ảnh hưởng sang các trang vận hành (`STAFF`, `MANAGER`, `ADMIN`).
