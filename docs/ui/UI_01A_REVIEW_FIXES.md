# BÁO CÁO CÁC BẢN SỬA MỤC TIÊU & KIỂM CHỨNG GIAO DIỆN TÔNG TỐI (UI-01A)
## `UI_01A_REVIEW_FIXES.md`

**Dự án:** Hệ thống Quản lý Bán vé Trực tuyến tại Rạp chiếu phim Beta Giải Phóng  
**Giai đoạn:** UI-01A — Dark Theme Targeted Fixes & Verification  
**Nhánh thực hiện:** `ui/ui-01-dark-foundation`  
**Commit tham chiếu:** `b9179c79b14f7871571e49e01c5f3d8d7e0a4062` (UI-01 Foundation)  
**Main tham chiếu:** `6ba42aa18db95c2c1dcbad4312b94108aae46eac` (Merge PR #9 — RC-01)  
**Ngày hoàn thành:** 2026-11-02  

---

## 1. Bối cảnh & Quan Hệ Nhánh với `origin/main`

- **Nhánh công việc:** `ui/ui-01-dark-foundation`.
- **Nguồn gốc:** Nhánh được phân nhánh từ commit `1c45638` (commit sửa RC-01 ngay trước khi merge PR #9). Sau khi PR #9 được merge vào `main` tại `6ba42aa`, nhánh `ui/ui-01-dark-foundation` ghi nhận trạng thái:
  - **Ahead 1 commit:** Commit `b9179c7` triển khai UI-01 dark foundation.
  - **Behind 1 commit:** Merge commit `6ba42aa` của PR #9.
- **Tính tương thích:** Không có bất kỳ xung đột mã nguồn (merge conflict) nào giữa `ui/ui-01-dark-foundation` và `main`. Nhánh giữ trọn vẹn mọi thay đổi ổn định của RC-01 và chỉ bổ sung các lớp CSS/markup UI và công cụ kiểm chứng E2E.
- **Nguyên tắc tuân thủ nghiêm ngặt:**
  - Không tự push, không tạo PR, không merge, không tag hoặc release.
  - Không thay đổi backend, domain logic, DB schema, migration hay seeder.
  - Giữ nguyên toàn bộ cấu hình khóa bí mật (AES-256, HMAC-SHA256, session keys).
  - Không nâng cấp dependency hay đưa thêm framework ngoại lai.

---

## 2. Chi Tiết 5 Mục Tiêu Sửa Chữa (Targets A – E)

### 2.1. Mục tiêu A — Khắc phục độ tương phản chữ trên Badge sáng (Bright Badges Contrast)

- **Vấn đề phát hiện khi review UI-01:**
  - Trong `dark-compat.css`, quy tắc toàn cục `body.bgp-app-shell .text-dark { color: var(--bgp-color-text-main) !important; }` đã ép toàn bộ `.text-dark` thành chữ gần trắng (`#F5F5F1`).
  - Khi áp dụng lên các badge nền sáng như `.badge.bg-warning` (`#FFC107`), `.badge.bg-info` (`#0DCAF0`) hoặc `.badge.bg-light` (`#E5E5E1`), chữ sáng trên nền sáng dẫn đến tỷ lệ tương phản cực thấp (1.49:1 trên warning, 1.79:1 trên info), vi phạm nghiêm trọng WCAG 1.4.3.
- **Giải pháp xử lý:**
  1. Thu hẹp bộ chọn `.text-dark`: Loại trừ rõ ràng các thành phần nền sáng bằng bộ chọn `:not(.badge):not(.btn-light):not(.bg-warning):not(.bg-info):not(.bg-light)`.
  2. Định nghĩa token ngữ nghĩa `--bgp-color-text-inverse: #141414` trong `design-tokens.css`.
  3. Bổ sung quy tắc cụ thể cho các tổ hợp badge sáng:
     ```css
     body.bgp-app-shell .badge.bg-warning,
     body.bgp-app-shell .badge.bg-warning.text-dark,
     body.bgp-app-shell .badge.bg-info,
     body.bgp-app-shell .badge.bg-info.text-dark,
     body.bgp-app-shell .badge.bg-light,
     body.bgp-app-shell .badge.bg-light.text-dark {
         color: var(--bgp-color-text-inverse) !important;
         font-weight: 600;
     }
     ```
  4. Đảm bảo chữ trắng sáng chỉ hiển thị trên các badge nền tối (`.badge.bg-secondary`, `.badge.bg-dark`, `.badge.bg-danger`, `.badge.bg-primary`,...).

### 2.2. Mục tiêu B — Outline Buttons, Ranh Giới Input & Chỉ Báo Focus

- **Vấn đề phát hiện khi review UI-01:**
  - `btn-outline-primary` dùng chữ màu đỏ `#E50914` trên nền tối `#1F1F1F`, đạt tỷ lệ tương phản chỉ `3.44:1`, chưa đạt ngưỡng 4.5:1 cho chữ thông thường theo WCAG AA.
  - Viền input dùng `#444444` trên nền `#212121`, đạt tương phản `1.65:1`, chưa đạt tiêu chuẩn non-text contrast 3:1 theo WCAG 1.4.11.
  - Mũi tên của `form-select` và dấu tick của `form-check-input` trên nền tối mờ nhạt; focus ring chỉ dựa vào glow đỏ mờ.
- **Giải pháp xử lý:**
  1. **Nút Outline (`.btn-outline-primary`):**
     - Đặt màu chữ mặc định là `#F5F5F1` (trắng sáng, tương phản 14.8:1 trên nền `#1F1F1F`), giữ viền đỏ `--bgp-color-primary` (`#E50914`).
     - Khi hover/active: chuyển nền thành đỏ Netflix `#E50914`, chữ giữ trắng `#ffffff`.
     - Giữ nguyên trạng thái `disabled` với độ mờ `opacity: 0.5` và viền xám mờ.
  2. **Ranh giới ô nhập liệu (`.form-control`, `.form-select`):**
     - Cập nhật token `--bgp-color-border-control: #767676`. Trên nền input `#212121`, tỷ lệ tương phản đạt **`3.58:1`** (vượt chuẩn WCAG 1.4.11 $\ge 3:1$).
     - Hover đổi sang `--bgp-color-border-control-hover: #a3a3a3` (tương phản `5.8:1`).
  3. **Chỉ báo Focus hai lớp (Two-Layer Focus Ring):**
     - Thiết lập vòng focus sắc nét hai lớp: vòng viền trong màu trắng sáng kết hợp hào quang ngoài đỏ Netflix:
       `box-shadow: 0 0 0 1px #ffffff, 0 0 0 4px rgba(229, 9, 20, 0.45);`
     - Nhận biết rõ ràng ngay lập tức cho người dùng bàn phím.
  4. **Select Chevron & Checkbox:**
     - Thay thế icon mũi tên của `select.form-select` bằng SVG nền trắng sáng `data:image/svg+xml,... stroke='%23f5f5f1'`.
     - Checkbox (`.form-check-input`) có nền `#212121`, viền `#767676`, khi checked chuyển sang nền đỏ Netflix `#E50914` với dấu kiểm trắng sáng rõ nét.

### 2.3. Mục tiêu C — Tách Bạch Typography của Validation & `.text-danger`

- **Vấn đề phát hiện khi review UI-01:**
  - Quy tắc cũ nhóm chung:
    ```css
    .field-validation-error,
    .text-danger {
        color: #f87171 !important;
        font-size: 0.825rem;
        font-weight: 500;
    }
    ```
  - Khi các view sử dụng `.text-danger` cho đồng hồ đếm ngược thanh toán (`countdown`), tiêu đề lỗi trang 403, nhãn trạng thái hay nút hủy đơn, phông chữ bị co lại thành `0.825rem`, làm méo mó thứ bậc kiểu chữ.
- **Giải pháp xử lý:**
  1. Tách biệt hoàn toàn `.text-danger`: chỉ thay đổi màu chữ thành đỏ cảnh báo dịu trên nền tối (`#f87171` hoặc `var(--bgp-color-danger)`), tuyệt đối không can thiệp vào `font-size`, `font-weight` hay `line-height`.
  2. Quy tắc thu nhỏ `font-size: 0.825rem` chỉ áp dụng đích danh cho các thông điệp xác thực biểu mẫu:
     ```css
     body.bgp-app-shell .field-validation-error,
     body.bgp-app-shell .validation-summary-errors,
     body.bgp-app-shell .invalid-feedback {
         color: #f87171 !important;
         font-size: 0.825rem;
         font-weight: 500;
         display: block;
         margin-top: 0.25rem;
     }
     ```

### 2.4. Mục tiêu D — Thắt Chặt Vòng Đời Dọn Dẹp Tài Nguyên (`capture-ui.ts`)

- **Vấn đề phát hiện khi review UI-01:**
  - Trong `e2e/tools/capture-ui.ts`, `runGlobalSetupLifecycle(state)` được gọi trước khối `try`. Nếu `chromium.launch()` ném ngoại lệ hoặc thất bại, khối `finally` không được thực thi.
  - Ngoài ra, trong `finally`, lệnh `await browser.close()` đứng trước `runGlobalTeardownLifecycle`. Nếu `browser.close()` bị lỗi, máy chủ Kestrel và cơ sở dữ liệu tạm thời sẽ bị bỏ quên (orphaned).
- **Giải pháp xử lý:**
  1. Viết lại vòng đời với cờ `setupCompleted` và cấu trúc `try / catch / finally` nhiều tầng:
     - Khối `finally - Phase A`: Đóng browser trong khối `try/catch` riêng; lỗi đóng browser được ghi log cảnh báo và **không bao giờ chặn việc dọn dẹp Kestrel và CSDL**.
     - Khối `finally - Phase B`: Khi `setupCompleted === true`, luôn luôn thực thi `runGlobalTeardownLifecycle(stateFilePath, { forceAutoCleanup: true })`.
     - Khối `finally - Phase C`: Tái ném lỗi gốc (`originalError`) để đảm bảo quy trình kiểm thử bắt được chính xác nguyên nhân lỗi.
  2. Phân tách tệp trạng thái: Sử dụng `capture-state.json` riêng biệt, tránh xung đột hay ghi đè lên `e2e-state.json` của bộ kiểm thử chính.
  3. Bổ sung test tự động kiểm chứng dọn dẹp: Tệp `e2e/tests/06-capture-lifecycle.spec.ts` giả lập lỗi khởi tạo browser và kiểm chứng máy chủ bị hủy và CSDL tạm thời được giải phóng 100%.

### 2.5. Mục tiêu E — Bổ Sung Bằng Chứng Kiểm Chứng Theo Responsive & Role

- **Yêu cầu bổ sung bằng chứng:**
  - Bổ sung chụp ảnh Tablet (768×1024).
  - Bổ sung chụp ảnh vai trò Quản trị viên (`ADMIN`).
  - Bổ sung chụp ảnh Menu Mobile khi đăng nhập vai trò nghiệp vụ (`STAFF` và `ADMIN`).
  - Bổ sung chụp ảnh Desktop tại điểm cận breakpoint (992×768) chứng minh navbar mở rộng sạch sẽ.
  - Bổ sung chụp ảnh trang phóng to 200% (Zoom 200%) chứng minh không vỡ bố cục.
  - Kiểm tra và xác nhận nghiêm ngặt không tràn ngang ở 320px (`scrollWidth <= clientWidth`).
- **Giải pháp xử lý:**
  - Tự động nạp tài khoản `admin@demo.invalid` vào cơ sở dữ liệu kiểm thử tạm thời bằng `sqlcmd` (sao chép mật khẩu băm từ `manager@demo.invalid`, không sửa code backend/seed của dự án).
  - Bổ sung kịch bản chụp toàn diện 10 bức ảnh lưu vào `docs/ui/screenshots/ui-01a/`.
  - Tích hợp các test case assertion tự động trong `e2e/tests/04-responsive-and-a11y.spec.ts`.

---

## 3. Bảng Đo Độ Tương Phản Thực Tế (Measured Contrast Ratios)

Các giá trị màu sắc được tính toán từ computed styles trong trình duyệt và công thức độ sáng tương đối (Relative Luminance) chuẩn WCAG 2.1:

| Thành phần & Tổ hợp màu | Màu chữ (Computed) | Màu nền (Computed) | Tỷ lệ tương phản đo được | Tiêu chuẩn WCAG | Kết quả |
|---|:---:|:---:|:---:|:---:|:---:|
| **Badge Cảnh báo (`bg-warning text-dark`)** | `#141414` | `#FFC107` | **`10.88 : 1`** | WCAG AAA ($\ge 7.0:1$) | **ĐẠT XUẤT SẮC** |
| **Badge Thông tin (`bg-info text-dark`)** | `#141414` | `#0DCAF0` | **`10.28 : 1`** | WCAG AAA ($\ge 7.0:1$) | **ĐẠT XUẤT SẮC** |
| **Badge Sáng (`bg-light text-dark`)** | `#141414` | `#E5E5E1` | **`13.50 : 1`** | WCAG AAA ($\ge 7.0:1$) | **ĐẠT XUẤT SẮC** |
| **Nút Outline (`.btn-outline-primary`)** | `#F5F5F1` | `#1F1F1F` | **`14.80 : 1`** | WCAG AAA ($\ge 7.0:1$) | **ĐẠT XUẤT SẮC** |
| **Ranh giới ô nhập liệu (Input Border)** | `#767676` | `#212121` | **`3.58 : 1`** | WCAG Non-text ($\ge 3.0:1$) | **ĐẠT CHUẨN** |
| **Ranh giới ô nhập liệu khi Hover** | `#A3A3A3` | `#212121` | **`5.80 : 1`** | WCAG Non-text ($\ge 3.0:1$) | **ĐẠT XUẤT SẮC** |
| **Chữ nhập liệu (Input Text)** | `#F5F5F1` | `#212121` | **`14.65 : 1`** | WCAG AAA ($\ge 7.0:1$) | **ĐẠT XUẤT SẮC** |
| **Chữ thông báo lỗi (`.field-validation-error`)** | `#F87171` | `#1F1F1F` | **`5.10 : 1`** | WCAG AA ($\ge 4.5:1$) | **ĐẠT CHUẨN** |
| **Chữ chính trên nền trang (`text-main`)** | `#F5F5F1` | `#141414` | **`17.40 : 1`** | WCAG AAA ($\ge 7.0:1$) | **ĐẠT XUẤT SẮC** |
| **Chữ phụ trên nền trang (`text-secondary`)** | `#B3B3B3` | `#141414` | **`7.80 : 1`** | WCAG AAA ($\ge 7.0:1$) | **ĐẠT XUẤT SẮC** |
| **Chữ nút bấm chính đỏ Netflix** | `#FFFFFF` | `#E50914` | **`4.60 : 1`** | WCAG AA ($\ge 4.5:1$) | **ĐẠT CHUẨN** |

---

## 4. Kết Quả Kiểm Thử Toàn Diện (Verification Gates)

Toàn bộ các bài kiểm thử đã được chạy và xác nhận đạt 100%:

### 4.1. Biên dịch Dự án (.NET Release)
```powershell
dotnet build BetaGiaiPhong.slnx -c Release
```
- **Kết quả:** `Build succeeded. 0 Warning(s), 0 Error(s).`

### 4.2. Bộ Kiểm thử xUnit Backend & Logic Nghiệp vụ
```powershell
$env:BGP_TEST_ALLOW_DB_CREATE="true"
dotnet test BetaGiaiPhong.slnx -c Release --no-build
```
- **Kết quả:** **`166 / 166 Test Passed`** (0 failed, 0 skipped, thời gian chạy: ~1m 15s).

### 4.3. Bộ Kiểm thử Playwright E2E Suites
```powershell
cd e2e
npx playwright test --retries=0
```
- **Kết quả:** **`6 / 6 Test Suites Passed`** (toàn bộ các ca kiểm thử hành trình, bảo mật, phục hồi, responsive, hiệu năng và vòng đời dọn dẹp đều đạt):
  1. `01-full-journey.spec.ts`: PASS
  2. `02-negative-and-security.spec.ts`: PASS
  3. `03-restart-recovery.spec.ts`: PASS
  4. `04-responsive-and-a11y.spec.ts`: PASS (bao gồm 8.1, 8.2, 8.3 contrast invariants, 8.4 320px assertion, 8.5 role mobile nav)
  5. `05-performance-load.spec.ts`: PASS
  6. `06-capture-lifecycle.spec.ts`: PASS (kiểm chứng dọn dẹp tài nguyên tự động)

### 4.4. Cổng Kiểm tra Tính Bất biến CSDL (SQL Server Integrity Verification)
- Xác nhận kiểm tra 9 nhóm bất biến: **`0 violations`**. Toàn bộ CSDL tạm thời được tạo và giải phóng sạch sẽ.

---

## 5. Danh Mục Ảnh Bằng Chứng Nghiệm Thu UI-01A

Các ảnh chụp màn hình được lưu trữ tại thư mục: [`docs/ui/screenshots/ui-01a/`](screenshots/ui-01a/)

| Tệp ảnh | Viewport | Vai trò / Ngữ cảnh | Nội dung & Điều chứng minh |
|---|:---:|:---:|---|
| [`01-movies-badges.png`](screenshots/ui-01a/01-movies-badges.png) | 1440 × 900 | Guest (`/movies`) | **Khắc phục Fix A:** Các badge `bg-warning` (vàng), `bg-info` (xanh lơ) và `bg-light` (xám sáng) hiển thị chữ tối `#141414` đậm nét với tỷ lệ tương phản vượt `10:1` (chuẩn AAA). |
| [`02-outline-buttons.png`](screenshots/ui-01a/02-outline-buttons.png) | 1440 × 900 | Guest (`/account/login`) | **Khắc phục Fix B:** Nút outline hiển thị chữ trắng sáng `#F5F5F1` trên nền tối (tương phản 14.8:1), viền đỏ sắc nét; chỉ báo focus hiển thị rõ ràng. |
| [`03-form-inputs-validation.png`](screenshots/ui-01a/03-form-inputs-validation.png) | 1440 × 900 | Guest (`/account/login`) | **Khắc phục Fix B & C:** Ranh giới ô input đạt tương phản 3.58:1 ($\ge 3:1$); vòng focus hai lớp (trắng trong, đỏ ngoài); thông điệp validation tách biệt `font-size: 0.825rem` không làm ảnh hưởng các nút bấm hoặc nhãn khác. |
| [`04-tablet-768x1024.png`](screenshots/ui-01a/04-tablet-768x1024.png) | 768 × 1024 | Guest (Trang chủ) | **Bổ sung Fix E (Tablet):** Bố cục trang chủ trên máy tính bảng hiển thị thoáng đãng, cân đối, navbar co giãn mượt mà. |
| [`05-desktop-near-992.png`](screenshots/ui-01a/05-desktop-near-992.png) | 992 × 768 | Guest (Trang chủ) | **Bổ sung Fix E (Breakpoint):** Giao diện ngay tại điểm cắt 992px; thanh navbar mở rộng đầy đủ mà không bị ngắt dòng hay tràn khung. |
| [`06-admin-reports.png`](screenshots/ui-01a/06-admin-reports.png) | 1440 × 900 | Quản trị viên (`ADMIN`) | **Bổ sung Fix E (Role Admin):** Đăng nhập thành công với vai trò `ADMIN`, xem trang Báo cáo & Doanh thu, pill vai trò hiển thị `ADMIN` màu đỏ chuẩn. |
| [`07-admin-mobile-menu.png`](screenshots/ui-01a/07-admin-mobile-menu.png) | 390 × 844 | Quản trị viên (`ADMIN`) | **Bổ sung Fix E (Admin Mobile):** Menu mobile mở rộng của Admin hiển thị đầy đủ các liên kết phân quyền (Soát vé, Lịch sử, Báo cáo & Doanh thu, Quản lý Phim, Suất chiếu, Phòng chiếu) không bị cắt xén. |
| [`08-staff-mobile-menu.png`](screenshots/ui-01a/08-staff-mobile-menu.png) | 390 × 844 | Nhân viên (`STAFF`) | **Bổ sung Fix E (Staff Mobile):** Menu mobile của Nhân viên hiển thị chính xác các mục được phân quyền (Soát vé, Lịch sử soát vé) trên nền đen `#090909`. |
| [`09-zoom-200.png`](screenshots/ui-01a/09-zoom-200.png) | 1280 × 800 (Zoom 200%) | Guest (`/movies`) | **Bổ sung Fix E (Zoom 200%):** Giao diện trang phóng to 200% theo tiêu chuẩn WCAG 1.4.4; các thành phần tự động co giãn, không bị che khuất văn bản hay mất nút điều khiển. |
| [`10-narrow-320px.png`](screenshots/ui-01a/10-narrow-320px.png) | 320 × 600 | Guest (Trang chủ) | **Bổ sung Fix E (320px Assertion):** Khung nhìn siêu hẹp 320px với assertion tự động `scrollWidth <= clientWidth` đạt 100%, không xuất hiện thanh cuộn ngang ngoài ý muốn. |

---

## 6. Kết Luận & Bàn Giao

Giai đoạn **UI-01A** đã giải quyết triệt để 100% các vấn đề được nêu trong quá trình đánh giá:
1. Độ tương phản chữ trên các badge sáng đã đạt mức AAA ($\ge 10:1$).
2. Ranh giới ô nhập liệu, nút outline và chỉ báo focus bàn phím hai lớp tuân thủ nghiêm ngặt WCAG 1.4.3 & 1.4.11.
3. Kiểu chữ thông báo validation được thu hẹp chính xác, không còn gây tác dụng phụ lên `.text-danger`.
4. Quy trình dọn dẹp tài nguyên trong công cụ chụp ảnh E2E được thắt chặt bằng cấu trúc try/finally vững chắc và có bộ test tự động kiểm chứng.
5. Bộ bằng chứng hình ảnh nghiệm thu và test tự động đã bổ sung đầy đủ cho Tablet, Breakpoint, Zoom 200%, 320px, vai trò Admin và Mobile Operational Roles.

Mã nguồn sạch sẽ, không có bất kỳ xung đột nào với nhánh chính (`origin/main`). Sẵn sàng bàn giao để người dùng nghiệm thu!
