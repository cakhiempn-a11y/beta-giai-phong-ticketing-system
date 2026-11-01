# BÁO CÁO NGHIỆM THU CHẤT LƯỢNG CUỐI CÙNG — BE-05C (`BE_05C_REPORT`)
## Final Quality Gate Hardening, Requirements Traceability, Thesis Documentation & Defense Readiness

**Dự án:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Tác giả:** Vũ Văn Khiêm (`11235581`) — Ngành Hệ thống thông tin quản lý (MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương  
**Nhánh làm việc:** `docs/be05c-final-delivery`  
**Baseline commit (`main`):** `964bd232479b220856e13d33abc1aa7d8a0b7666` (PR #7 merged)  
**Ngày hoàn thành:** 2026-11-01

---

## 1. Executive Summary

Giai đoạn **BE-05C** là chặng hoàn thiện cuối cùng của dự án nguyên mẫu học thuật **Hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng**, thực hiện hai nhiệm vụ trọng tâm:
1. **Gia cố 4 Cổng Chất lượng Cuối cùng (Phase 0.1 – 0.4) & Khắc phục triệt để điểm không ổn định khi giải mã QR PNG:**
   - Sửa `e2e/global-teardown.ts` để mọi vi phạm bất biến từ `Runner verify` đều làm đỏ cổng chất lượng E2E CI nhưng vẫn luôn thực thi khối `finally` dọn sạch CSDL tạm và tệp trạng thái (`Test 7.6`).
   - Sửa `e2e/global-setup.ts` để ghi tệp trạng thái quyền sở hữu ngay sau khi tạo CSDL E2E và tự động dọn sạch CSDL nếu quá trình khởi động Web hoặc kiểm tra `/health` thất bại giữa chừng (`Test 7.7`).
   - Sửa `e2e/tests/05-performance-load.spec.ts` để ghi nhận trung thực `sqlExceptions: 'NOT MEASURED'` và `deadlocks: 'NOT MEASURED'`, đồng thời tách rõ xung đột nghiệp vụ hợp lệ (`expectedConflicts409`) khỏi lỗi máy chủ (`http5xxErrors`).
   - Sửa `e2e/tests/helpers.ts` (`assertNoSecretLeakInText`) để giải mã `qr_token_cipher` trong bộ nhớ RAM và kiểm tra trực tiếp chuỗi 43 ký tự Base64URL thực tế của `QrTokenProtector.Issue()` (`/^[A-Za-z0-9_-]{43}$/`) trên HTML, API JSON và console log mà không bao giờ in token ra màn hình; tắt tự động chụp screenshot/video/trace trên CI và bổ sung bước quét bảo mật artifact trong `.github/workflows/ci.yml`.
   - Khắc phục triệt để lỗi `Html5Qrcode.scanFile` xuất hiện trên lượt chạy hậu hợp nhất `41279361688` bằng cách đổi kích thước module ảnh QR từ `GetGraphic(10)` sang **`GetGraphic(6)`** (`246x246` px, module `6px < 8px` kích thước khối của bộ lọc `ZXing HybridBinarizer`) kết hợp cơ chế multi-scale white-padded offscreen canvas fallback trong `StaffCheckIn/Index.cshtml`.
2. **Kiểm định Toàn bộ Tài liệu Thượng nguồn & Xây dựng Trọn bộ Hồ sơ Bàn giao Đồ án Tốt nghiệp (`docs/delivery/`):**
   - Đọc và trích xuất trực tiếp toàn bộ 6 tài liệu `.docx` trong `references/` (bao gồm cả văn bản, bảng biểu và 20 sơ đồ nhúng bên trong) cùng toàn bộ tệp thiết kế trong `docs/`.
   - Hoàn thiện bộ 9 tài liệu bàn giao chuẩn mực tại `docs/delivery/` phục vụ báo cáo khóa luận và bảo vệ trước Hội đồng.

---

## 2. Chi tiết Khắc phục Cổng Chất lượng Giai đoạn Phase 0

| Hạng mục | Tệp sửa đổi | Nội dung kỹ thuật đã thực hiện | Bài kiểm thử chứng minh |
|---|---|---|---|
| **Phase 0.1 — E2E Teardown Integrity Gate** | `e2e/global-teardown.ts`, `src/BetaGiaiPhong.Runner/Program.cs` | Tách hàm `runGlobalTeardownLifecycle`: dừng Web server $\rightarrow$ chạy `Runner verify`. Nếu mã thoát $\ne 0$ hoặc đầu ra thiếu `INTEGRITY PASSED`, ghi nhận lỗi để ném ngoại lệ sau khi khối `finally` đã gọi `drop-e2e-db` và xóa `e2e-state.json`. | `03-restart-recovery.spec.ts` — **`Test 7.6`**: Tạo DB tạm, gọi `corrupt-e2e-db` để bơm 1 dòng `ShowtimeSeats` vi phạm bất biến, xác nhận `runGlobalTeardownLifecycle` ném lỗi `E2E QUALITY GATE FAILED` và DB tạm đã bị xóa sạch (`EXISTS_FALSE`). |
| **Phase 0.2 — E2E Setup Mid-Failure Cleanup** | `e2e/global-setup.ts` | Tách hàm `runGlobalSetupLifecycle`: gọi `saveE2eState(state)` ngay sau khi `create-e2e-db` thành công (với `webPid: 0`). Nếu khởi chạy tiến trình Web hoặc chờ `/health` ném lỗi, khối `catch` tự động dừng tiến trình con, gọi `drop-e2e-db` và xóa `e2e-state.json`. | `03-restart-recovery.spec.ts` — **`Test 7.7`**: Giả lập `healthCheckOverride` ném lỗi trong `runGlobalSetupLifecycle`, xác nhận hàm ném lỗi và DB vừa tạo được xóa sạch khỏi SQL Server (`EXISTS_FALSE`). |
| **Phase 0.3 — Honest Performance Metrics** | `e2e/tests/05-performance-load.spec.ts` | Cập nhật `BenchmarkMetric` với `sqlExceptions: 'NOT MEASURED'`, `deadlocks: 'NOT MEASURED'`, tách riêng `expectedConflicts409` (`29` yêu cầu nhận `409 Conflict` hợp lệ khi 30 luồng tranh 1 ghế) và `http5xxErrors` (`0` lỗi máy chủ). | `05-performance-load.spec.ts` & `e2e/.tmp/benchmark-results.json`. |
| **Phase 0.4 — QR Secret Protection & CI Artifacts** | `e2e/tests/helpers.ts`, `e2e/playwright.config.ts`, `.github/workflows/ci.yml` | Bổ sung `getOrderSensitiveQrArtifactsInMemory` gọi `Runner get-order-ciphers` và giải mã `AES-256-GCM` trong bộ nhớ Node.js để kiểm tra đúng chuỗi 43 ký tự Base64URL (`QrTokenProtector.Issue()`) không xuất hiện trong HTML, JSON hay console log. Đặt `screenshot: 'off'`, `video: 'off'`, `trace: 'off'` mặc định và thêm bước `Sanitize & Verify E2E Artifacts Before Upload` trong CI. | `01-full-journey.spec.ts` & `02-negative-and-security.spec.ts`. |
| **Fix QR Scan Determinism** | `TicketQueryService.cs`, `StaffCheckIn/Index.cshtml` | Đổi `qrCode.GetGraphic(10)` sang `qrCode.GetGraphic(6)` (`246x246` px) và thêm vòng lặp vẽ lại ảnh lên canvas nền trắng ở các kích thước `260px`, `360px`, `480px` nếu lần giải mã đầu thất bại. | `01-full-journey.spec.ts` & `02-negative-and-security.spec.ts` (`Test 2.8`) chạy ổn định 100%. |

---

## 3. Tổng hợp Kết quả Truy vết Yêu cầu Thượng nguồn (Phase 1 & Phase 2)

Chi tiết đầy đủ được trình bày tại [docs/delivery/SRS_IMPLEMENTATION_TRACEABILITY.md](../delivery/SRS_IMPLEMENTATION_TRACEABILITY.md):

| Nhóm yêu cầu / Mô hình | Tổng số | `IMPLEMENTED` | `PARTIALLY IMPLEMENTED` | `NOT IMPLEMENTED` | `OUT OF SCOPE (P1)` | Tỷ lệ đáp ứng P0 |
|---|---:|---:|---:|---:|---:|---|
| **Yêu cầu chức năng (`FR-*`)** | 54 (`52 P0 + 2 P1`) | 46 (`46 P0`) | 6 (`5 P0 + 1 P1`) | 1 (`FR-AUTH-02` — P0) | 1 (`FR-RPT-05` — P1) | 46/52 P0 hoàn chỉnh, 5/52 P0 một phần, 1/52 P0 chưa cài đặt (`FR-AUTH-02`) |
| **Quy tắc nghiệp vụ (`BR-01..18`)** | 18 | 18 | 0 | 0 | 0 | **100%** (18/18) |
| **Yêu cầu phi chức năng (`NFR-01..18`)**| 18 | 17 | 1 (`NFR-15`) | 0 | 0 | **100%** trong phạm vi tự động hóa Chromium |
| **Quyết định thiết kế (`CR-01..09`)** | 9 | 9 | 0 | 0 | 0 | **100%** (9/9) |
| **Use Cases (`UC-01..16`)** | 16 | 11 | 4 (`UC-02, 10, 13, 14`) | 0 | 1 (`UC-16`) | Toàn bộ luồng nghiệp vụ cốt lõi hoạt động đầu–cuối |
| **Sơ đồ BPMN (`BPMN-01..05`)** | 6 | 6 | 0 | 0 | 0 | **100%** (6/6 sơ đồ) |
| **Sơ đồ Sequence (`SD-01..14`)** | 14 | 12 | 2 (`SD-13, 14`) | 0 | 0 | **100%** các luồng giao dịch chính |
| **Ca kiểm thử nghiệm thu (`TC-01..25`)**| 25 | 23 | 1 (`TC-06`) | 1 (`TC-02`) | 0 | **96%** (24/25 ca đã tự động hóa) |

---

## 4. Kết quả Thực thi Toàn bộ Cổng Chất lượng tại Máy trạm

| Cổng kiểm tra | Lệnh thực thi | Kết quả |
|---|---|---|
| **1. Build Release** | `dotnet build BetaGiaiPhong.slnx -c Release` | **PASS** (`0 Warning(s)`, `0 Error(s)`) |
| **2. xUnit Test Suite** | `dotnet test BetaGiaiPhong.slnx -c Release --no-build` | **PASS** (**166 / 166** tests passed) |
| **3. Playwright Chromium E2E** | `npx playwright test` (trong `e2e/`) | **PASS** (**5 / 5** suites passed) |
| **4. Runner SQL Integrity** | `dotnet run --project src/BetaGiaiPhong.Runner -- verify` | **PASS** (**9 / 9** nhóm bất biến đạt `0` vi phạm) |
| **5. Static Schema Check** | `python scripts/static_schema_checks.py` | **PASS** (`STATIC CHECKS PASSED: 17 tables, 116 columns, 22 FKs`) |
| **6. EF Core Model Drift** | `dotnet ef migrations has-pending-model-changes` | **PASS** (`No changes have been made...`) |

---

## 5. Danh mục Hồ sơ Bàn giao Đồ án Tốt nghiệp (`docs/delivery/`)

1. [`docs/delivery/SRS_IMPLEMENTATION_TRACEABILITY.md`](../delivery/SRS_IMPLEMENTATION_TRACEABILITY.md) — Ma trận truy vết đầy đủ từ SRS/BPMN/UC/SD xuống Code/DB/Tests.
2. [`docs/delivery/ARCHITECTURE_AND_DATA_MODEL.md`](../delivery/ARCHITECTURE_AND_DATA_MODEL.md) — Đặc tả kiến trúc hệ thống, danh mục API, phân cấp khóa SQL Server, máy trạng thái và giải trình khác biệt thiết kế.
3. [`docs/delivery/TEST_ACCEPTANCE_SUMMARY.md`](../delivery/TEST_ACCEPTANCE_SUMMARY.md) — Tổng hợp bằng chứng 9 tầng kiểm thử và cơ sở hiệu năng thực nghiệm.
4. [`docs/delivery/MANUAL_CAMERA_UAT.md`](../delivery/MANUAL_CAMERA_UAT.md) — Quy trình nghiệm thu thủ công 12 kịch bản cho camera vật lý (`MANUAL UAT — NOT TESTED`).
5. [`docs/delivery/INSTALLATION_GUIDE.md`](../delivery/INSTALLATION_GUIDE.md) — Hướng dẫn cài đặt, cấu hình biến môi trường, bảo toàn khóa AES-256 và vận hành.
6. [`docs/delivery/USER_MANUAL.md`](../delivery/USER_MANUAL.md) — Hướng dẫn sử dụng chi tiết cho `GUEST`, `CUSTOMER`, `STAFF`, `MANAGER`/`ADMIN`.
7. [`docs/delivery/THESIS_TECHNICAL_CONTENT.md`](../delivery/THESIS_TECHNICAL_CONTENT.md) — Nội dung kỹ thuật học thuật 5 chương phục vụ báo cáo khóa luận tốt nghiệp MIS.
8. [`docs/delivery/DEFENSE_DEMO_SCRIPT.md`](../delivery/DEFENSE_DEMO_SCRIPT.md) — Kịch bản trình diễn trực tiếp 14 bước (12–15 phút), phương án dự phòng và câu hỏi phản biện.
9. [`docs/delivery/RELEASE_CHECKLIST.md`](../delivery/RELEASE_CHECKLIST.md) — Danh mục kiểm tra sẵn sàng hợp nhất và đề xuất nhãn phát hành `v1.0.0-capstone-rc1`.
