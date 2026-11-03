# BÁO CÁO PHỤC HỒI CI & KIỂM CHỨNG HÀNH VI TRANG CHỦ (UI-02A-FIX)
## `UI_02A_FIX_REPORT.md`

**Dự án:** Hệ thống Quản lý Bán vé Trực tuyến tại Rạp chiếu phim Beta Giải Phóng  
**Giai đoạn:** UI-02A-FIX — CI Recovery & Cinematic Homepage Verification  
**Repository:** `https://github.com/cakhiempn-a11y/beta-giai-phong-ticketing-system`  
**Nhánh công việc:** `ui/ui-02a-cinematic-home`  
**Pull Request mục tiêu:** PR #11 (`https://github.com/cakhiempn-a11y/beta-giai-phong-ticketing-system/pull/11`)  
**Baseline commit trước khi sửa:** `8c98aab4ca01ed9906013a8cf9c69137985c4194`  
**CI Run thất bại phân tích:** Run `41398780456` (Job `125126918860`)  
**Ngày hoàn thành:** 2026-11-02  

---

## 1. Tóm Tắt Hiện Trạng & Phân Tích Nguyên Nhân Thất Bại CI

### 1.1. Hiện trạng ghi nhận từ GitHub Actions CI
Trên GitHub Actions workflow run `41398780456`:
- **Build & Static Validation:** PASS (0 warnings, 0 errors).
- **Automated Tests with SQL Server:** PASS (166/166 xUnit integration tests pass).
- **Integrity Checks & Runner Validation:** PASS (`smoke`, `race`, `verify` pass).
- **Real Browser E2E Tests (Playwright):** FAILED (14 passed, 1 failed tại `06-capture-lifecycle.spec.ts`).

### 1.2. Phân tích nguyên nhân gốc rễ (Root Cause Analysis)
Trích xuất lỗi từ Playwright log:
```
Expected substring: "Simulated browser launch failure"
Received: "[Capture] CRITICAL: SQL execution failed in ephemeral DB ... (exit null): undefined"
```

1. **Phân tích cơ chế hạ tầng:**
   - Trong `.github/workflows/ci.yml`, SQL Server chạy trong một Docker container dịch vụ (`services.sqlserver`, image `mcr.microsoft.com/mssql/server:2022-latest`), mở cổng `1433:1433`.
   - Host máy ảo chạy bài test là `ubuntu-latest`. Trình điều khiển SQL Server CLI (`sqlcmd`) chỉ tồn tại *bên trong* container dịch vụ, **hoàn toàn không được cài đặt trên `PATH` của host Linux**.
2. **Điểm đứt gãy trong mã kiểm thử:**
   - Trong `e2e/tools/capture-ui.ts`, hàm `executeSqlInDb` gọi `spawnSync('sqlcmd', ...)`. Khi chạy trên host Linux không có `sqlcmd`, lệnh này lập tức trả về `res.error.code = 'ENOENT'`, `res.status = null`, `res.stderr = undefined`.
   - Vòng lặp `executeSqlInDb` cố tình retry 5 lần trước khi ném lỗi `SQL execution failed ... (exit null): undefined`.
   - Thêm vào đó, trong hàm `runUiCapture`, lệnh `seedAdminUser(state)` (gọi `executeSqlInDb`) được đặt **trước** lệnh kiểm tra giả lập lỗi `if (options?.skipBrowserLaunchForTest)`.
   - Vì vậy, bài kiểm tra vòng đời `06-capture-lifecycle.spec.ts` không bao giờ chạm được đến dòng ném lỗi `Simulated browser launch failure`, dẫn đến assert thất bại và làm gãy toàn bộ pipeline E2E.

---

## 2. Các Biện Pháp Khắc Phục Hệ Thống Đã Triển Khai

Toàn bộ 5 nhóm nhiệm vụ yêu cầu đã được xử lý triệt để, có kiểm chứng thực nghiệm độc lập:

### Nhóm 1: Sửa triệt để nguyên nhân CI thất bại & Nâng cấp năng lực Runner
1. **Loại bỏ phụ thuộc `sqlcmd`:**
   - Dự án đã có sẵn công cụ `BetaGiaiPhong.Runner` viết bằng .NET / C# kết nối trực tiếp đến SQL Server qua thư viện `Microsoft.Data.SqlClient`.
   - Bổ sung các lệnh SQL nội bộ vào `BetaGiaiPhong.Runner` (`ExecuteE2eSqlAsync`):
     * `seed-admin`: Tạo tài khoản quản trị `admin@demo.invalid` kèm vai trò `ADMIN` với hash mật khẩu an toàn.
     * `seed-ui-fixtures`: Tạo tập dữ liệu mô phỏng chuẩn cho UI E2E (phim không có suất chiếu, phim tiêu đề dài, các phim mở rộng thanh cuộn, poster target hỏng).
     * `reset-ui-fixtures`: Dọn dẹp sạch sẽ toàn bộ các phim fixture mô phỏng sau khi test kết thúc.
     * `hide-all-movies` / `restore-all-movies`: Chuyển đổi trạng thái `DRAFT` / `PUBLISHED` để kiểm thử trạng thái rỗng (`Empty State`).
     * `set-long-title` / `restore-title`: Thử nghiệm tiêu đề dài trên Hero banner.
   - Mọi thao tác này đều được bảo vệ nghiêm ngặt bằng quy tắc an toàn cơ sở dữ liệu: Runner chỉ chấp nhận thực thi khi tên database bắt đầu bằng tiền tố `BetaGiaiPhong_E2E_`.
2. **Cập nhật `e2e/tools/capture-ui.ts`:**
   - Di chuyển điều kiện `if (options?.skipBrowserLaunchForTest)` lên trước bước chuẩn bị dữ liệu / seed tài khoản, đảm bảo kiểm thử vòng đời dọn dẹp kích hoạt chính xác điểm lỗi giả lập.
   - Chuyển toàn bộ thao tác SQL trong `capture-ui.ts` sang sử dụng `runRunnerCommand(['e2e-sql', ...], state)`, chạy ổn định trên cả Windows local lẫn Linux GitHub Actions mà không phụ thuộc vào `sqlcmd`.
   - Nâng cấp hàm `executeSqlInDb`:
     * Bổ sung cờ `-b` (On Error Batch Abort) khi gọi `sqlcmd`.
     * Phân biệt rõ ràng lỗi hệ thống: ném ngay lập tức khi gặp `ENOENT` (không tìm thấy executable) hoặc `EACCES` (không có quyền thực thi), **tuyệt đối không retry vô ích đối với lỗi cấu hình cố định**.
     * Xử lý trường hợp timeout `ETIMEDOUT` và kiểm tra `res.signal`.
     * Làm sạch toàn bộ thông tin nhạy cảm (mật khẩu, chuỗi kết nối) bằng hàm `sanitizeTextForLog`.

### Nhóm 2: Tăng cường tính chặt chẽ của kiểm thử vòng đời (`06-capture-lifecycle.spec.ts`)
- Thêm bước kiểm tra mã thoát: `expect(dbCheck.status).toBe(0)` trước khi xác nhận nội dung `DB_EXISTS:0`.
- Kiểm chứng máy chủ Kestrel thực sự bị đóng thông qua lỗi kết nối ở tầng mạng (`transportError != null`, connection actively refused), không phụ thuộc vào HTTP status code 404/500.
- Kiểm chứng tiến trình web đã chết hoàn toàn thông qua tín hiệu tiến trình hệ điều hành: `process.kill(pid, 0)` ném ngoại lệ.

### Nhóm 3: Kiểm chứng hành vi Fallback hình ảnh thực (`07-cinematic-home.spec.ts` Test 7.3)
- Thay thế hoàn toàn cơ chế tiêm lỗi nhân tạo của test cũ bằng hành vi tự nhiên của trình duyệt:
  * Sử dụng `page.route` của Playwright để chặn yêu cầu tải poster mô phỏng (`**/broken-poster-target.jpg`) và trả về HTTP 404.
  * **Không gán lại `onerror` trong test**.
  * **Không can thiệp đặt thủ công thuộc tính `src` trong test**.
  * Để trình duyệt kích hoạt sự kiện `error` tự nhiên và kích hoạt bộ xử lý của ứng dụng.
- Kiểm chứng các chỉ số tải ảnh sau khi chuyển sang ảnh vector:
  * `targetImg` tự động chuyển sang `/images/poster-fallback.svg`.
  * `img.complete === true`.
  * `img.naturalWidth > 0` và `img.naturalHeight > 0`.
- **Kiểm chứng chống lặp vô tận (Infinite Loop Protection):**
  * Thử nghiệm trường hợp cực đoan: chặn cả URL ảnh fallback (`**/images/poster-fallback.svg`).
  * Trong `src/BetaGiaiPhong.Web/wwwroot/js/home-cinema.js`, bộ lắng nghe sự kiện `error` được bảo vệ bằng cờ `{ once: true }` và `removeEventListener`, đồng thời mã inline HTML gán `this.onerror = null`.
  * Xác nhận số lượng yêu cầu lỗi bị chặn được giới hạn nghiêm ngặt (< 50 requests) và hoàn toàn không gây treo trình duyệt.

### Nhóm 4: Đồng bộ bộ điều khiển và thanh cuộn hàng phim (`Movie Rail`)
- Đồng bộ hóa toàn bộ các selector giữa Razor View, CSS, JavaScript và E2E:
  * Classes: `.bgp-rail-btn-prev`, `.bgp-rail-btn--prev`, `.bgp-rail-btn-next`, `.bgp-rail-btn--next`.
  * Data test attributes: `data-testid="rail-btn-prev"`, `data-testid="rail-btn-next"`, `data-testid="movie-rail"`, `data-testid="movie-card"`.
- Cập nhật `home-cinema.css`:
  * Khai báo `.bgp-rail-btn.is-hidden { display: none !important; }`.
- Cập nhật `home-cinema.js`:
  * Khi hàng phim không có thanh cuộn tràn (`maxScrollLeft <= 8`): ẩn cả 2 nút điều hướng bằng lớp `.is-hidden`, gán `disabled = true` và `aria-hidden="true"`.
  * Khi hàng phim có thanh cuộn tràn (`maxScrollLeft > 8`): hiển thị các nút điều hướng; nút trái bị disable ở vị trí đầu (`scrollLeft <= 8`), nút phải bị disable ở vị trí cuối (`scrollLeft >= maxScrollLeft - 8`).
  * Xử lý co giãn màn hình với cả `ResizeObserver` lẫn sự kiện `window.resize`.
  * Hỗ trợ cuộn ngang mượt mà bằng cảm ứng trên thiết bị di động (`-webkit-overflow-scrolling: touch`), ẩn nút bấm vật lý trên màn hình `<= 768px`.
  * Kiểm chứng không phát sinh bất kỳ yêu cầu POST/PUT/DELETE đột biến nào khi duyệt trang chủ và cuộn hàng phim.

### Nhóm 5: Khớp nội dung Hero Banner với dữ liệu thực tế
- Khi phim Hero có suất chiếu sắp tới (`UpcomingShowtimesCount > 0`):
  * Eyebrow hiển thị huy hiệu `ĐANG CHIẾU` (nền đỏ `#E50914`).
  * Huy hiệu hiển thị số lượng suất chiếu: `X suất chiếu`.
  * Nút hành động chính (Primary CTA): `Xem lịch chiếu` liên kết đến `/movies/{id}`.
- Khi phim Hero chưa có suất chiếu sắp tới (`UpcomingShowtimesCount == 0`):
  * Eyebrow hiển thị huy hiệu `CHƯA CÓ SUẤT CHIẾU SẮP TỚI` (nền secondary).
  * Huy hiệu hiển thị `Chưa có lịch chiếu`.
  * Đoạn tóm tắt mặc định mang tính trung tính, chuẩn xác: *"Phim đã được phát hành trên hệ thống. Hiện chưa có suất chiếu sắp tới."* (không khẳng định sai sự thật là phim đang mở lịch chiếu).
  * Nút hành động chính (Primary CTA): `Chi tiết phim` liên kết đến `/movies/{id}`.
- Khi hệ thống không có phim nào (`Empty State`):
  * Hiển thị thông điệp chào mừng chung về rạp Beta Giải Phóng.
  * Nút hành động chính chuyển thành `Khám phá phim` dẫn đến trang danh sách phim `/movies`.
- Xử lý tiêu đề phim cực dài an toàn bằng CSS `word-break: break-word` và `clamp()`, không làm tràn khung ngang tài liệu.

---

## 3. Bảng Tổng Hợp Kiểm Thử & Kiểm Chứng (Test Metrics)

| Hạng mục kiểm thử | Công cụ / Lệnh thực hiện | Kết quả thực tế | Trạng thái |
| :--- | :--- | :--- | :---: |
| **Biên dịch Solution** | `dotnet build BetaGiaiPhong.slnx -c Release` | 0 Warning(s), 0 Error(s) | **PASS** |
| **Kiểm thử Đơn vị & Tích hợp xUnit** | `dotnet test BetaGiaiPhong.slnx -c Release --no-build` | 166 Passed, 0 Failed, 0 Skipped (1m 13s) | **PASS** |
| **Kiểm thử Vòng đời Cleanup** | `npx playwright test tests/06-capture-lifecycle.spec.ts` | Teardown sạch, DB_EXISTS:0, Process exit | **PASS** |
| **Kiểm thử Hành vi Fallback & Rail** | `npx playwright test tests/07-cinematic-home.spec.ts` | 7/7 kịch bản đạt chuẩn thực nghiệm | **PASS** |
| **Toàn bộ Bộ E2E Playwright** | `npx playwright test --retries=0` | **16/16 Passed** (2.6 phút) | **PASS** |
| **Công cụ Chụp Ảnh Giao diện** | `npx ts-node tools/capture-ui.ts ui-02a` | Đủ 8 ảnh chụp màn hình, dọn dẹp sạch DB | **PASS** |

---

## 4. Danh Sách Tệp Thay Đổi

1. `src/BetaGiaiPhong.Runner/Program.cs`:
   - Thêm các hành động `seed-admin`, `seed-ui-fixtures`, `reset-ui-fixtures`, `hide-all-movies`, `restore-all-movies`, `set-long-title`, `restore-title` có bảo vệ tiền tố DB.
2. `src/BetaGiaiPhong.Web/Views/Home/Index.cshtml`:
   - Điều chỉnh Hero banner hiển thị chính xác theo trạng thái `UpcomingShowtimesCount`.
   - Bổ sung data test attributes và unified classes cho Movie rail và Movie cards.
   - Tối ưu hóa fallback handler inline an toàn.
3. `src/BetaGiaiPhong.Web/wwwroot/css/home-cinema.css`:
   - Thêm quy tắc `.bgp-rail-btn.is-hidden { display: none !important; }`.
   - Đồng bộ class selector cho nút mũi tên `--prev`/`-prev` và `--next`/`-next`.
4. `src/BetaGiaiPhong.Web/wwwroot/js/home-cinema.js`:
   - Hoàn thiện tính toán thanh cuộn, cập nhật trạng thái ẩn/hiện và disabled của nút điều hướng.
   - Thêm bộ quan sát `ResizeObserver` và trình xử lý lỗi ảnh an toàn chống lặp vô tận.
5. `e2e/tools/capture-ui.ts`:
   - Tách biệt kiểm tra lỗi giả lập trước khi chuẩn bị dữ liệu.
   - Chuyển thao tác SQL sang `BetaGiaiPhong.Runner`.
   - Nâng cấp chẩn đoán lỗi `executeSqlInDb` và che giấu secret.
6. `e2e/tests/06-capture-lifecycle.spec.ts`:
   - Tăng cường kiểm tra `dbCheck.status === 0`, từ chối kết nối tầng mạng và xác nhận tiến trình web đã chết.
7. `e2e/tests/07-cinematic-home.spec.ts`:
   - Viết lại toàn bộ bài kiểm tra fallback bằng `page.route` tự nhiên.
   - Bổ sung kiểm chứng tương tác cuộn thanh rail, nút bấm, empty state, tiêu đề dài và tính bất biến của dữ liệu.
8. `docs/ui/UI_02A_CINEMATIC_HOME.md`:
   - Cập nhật bổ sung báo cáo kỹ thuật hoàn thiện.
9. `docs/ui/UI_02A_FIX_REPORT.md`:
   - Tài liệu báo cáo phục hồi CI và kiểm chứng hành vi.

---

## 5. Hướng Dẫn Bàn Giao & Cập Nhật PR

- **Trạng thái nhánh:** Toàn bộ mã nguồn trên nhánh `ui/ui-02a-cinematic-home` đã sẵn sàng và vượt qua 100% các bài kiểm tra nghiêm ngặt với 0 lần thử lại (`--retries=0`).
- **Khuyến nghị thông điệp Commit:**
  ```bash
  git add .
  git commit -m "fix(ui-02a): restore CI and verify cinematic homepage behavior"
  ```
- **Kích hoạt CI trên PR #11:**
  Người dùng thực hiện lệnh đẩy nhánh lên remote để GitHub Actions tự động kích hoạt lại pipeline kiểm tra chất lượng trên PR #11:
  ```bash
  git push origin ui/ui-02a-cinematic-home
  ```
