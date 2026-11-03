# DANH MỤC KIỂM TRA BÀN GIAO CUỐI CÙNG VÀ SẴN SÀNG HỢP NHẤT (`RELEASE_CHECKLIST`)

**Đề tài:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Tác giả:** Vũ Văn Khiêm (`11235581`) — Ngành Hệ thống thông tin quản lý (MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương  
**Nhánh bàn giao:** `fix/rc1-final-stabilization` (Baseline `main`: `102e633275e040fcc57be8e6276bd171d79de0fa`)  
**Nhãn phát hành đề xuất (Candidate Release Tag — chưa gắn thẻ):** `v1.0.0-capstone-rc1`

---

## 1. Bảng Kiểm tra 12 Cổng Chất lượng Bàn giao (Final Delivery Quality Gates)

| STT | Hạng mục kiểm tra (Checklist Item) | Lệnh / Phương pháp xác minh | Kết quả thực tế | Trạng thái |
|:---:|---|---|---|:---:|
| **1** | **Biên dịch cấu hình Release sạch** | `dotnet build BetaGiaiPhong.slnx -c Release` | `0 Warning(s)`, `0 Error(s)` trên cả 4 dự án (`Data`, `Runner`, `Web`, `Tests`). | ✅ **PASS** |
| **2** | **Kiểm thử xUnit (Unit + SQL Integration + Web)** | `dotnet test BetaGiaiPhong.slnx -c Release --no-build` | **173 / 173 bài kiểm thử PASS** (bao gồm 5 bài kiểm thử an toàn DB `TC_SAFE_01..05` và kiểm thử đồng bộ nguồn phim xác minh `TC_MGMT_25..26`). | ✅ **PASS** |
| **3** | **Kiểm thử trình duyệt thực Playwright Chromium E2E** | `npx playwright test` (tại thư mục `e2e/`) | **15 / 15 bộ spec (`50 / 50` bài kiểm thử) PASS** (bao gồm toàn bộ chuỗi nâng cấp giao diện Zero-Bootstrap `UI-P1`..`UI-P8` và cổng chất lượng `Test 7.6`, `Test 7.7`). | ✅ **PASS** |
| **4** | **Kiểm định 9 nhóm bất biến toàn vẹn SQL Server** | `dotnet run --project src/BetaGiaiPhong.Runner -- verify` | **9 / 9 nhóm bất biến đạt `0` vi phạm (`INTEGRITY PASSED`)**; nếu vi phạm sẽ làm đỏ E2E teardown (`Phase 0.1`). | ✅ **PASS** |
| **5** | **Kiểm tra tĩnh lược đồ CSDL (Static Schema Guard)**| `python scripts/static_schema_checks.py` | Xác nhận đúng **17 bảng**, **145 cột**, **39 SQL batches**, **22 khóa ngoại `NO ACTION`** và View `dbo.vw_ShowtimeSales`. | ✅ **PASS** |
| **6** | **Kiểm tra đồng bộ EF Core Model & Migrations** | `dotnet ef migrations has-pending-model-changes --project src/BetaGiaiPhong.Data --startup-project src/BetaGiaiPhong.Data` | Không có thay đổi mô hình nào chưa được đưa vào migration (`No changes have been made...`). | ✅ **PASS** |
| **7** | **Quét lỗ hổng bảo mật gói phụ thuộc (.NET & npm)** | `dotnet list BetaGiaiPhong.slnx package --vulnerable --include-transitive` & `npm audit` | **`0` gói NuGet có lỗ hổng**, **`0` gói npm có lỗ hổng**. | ✅ **PASS** |
| **8** | **Kiểm tra chống rò rỉ khóa bí mật & `qr_token`** | `assertNoSecretLeakInText` (giải mã và kiểm tra chuỗi 43 ký tự Base64URL trong RAM) + tắt tự động chụp screenshot/video/trace trên CI + bước quét artifact trong `.github/workflows/ci.yml` | **0 rò rỉ** khóa `BGP_QR_AES_KEY_B64`, `BGP_MOCK_HMAC_KEY_B64`, mật khẩu hay chuỗi 43 ký tự `qr_token` trong mã nguồn, DOM, API JSON hay log. | ✅ **PASS** |
| **9** | **Hoàn thiện bộ 9 tài liệu bàn giao (`docs/delivery/`) & Báo cáo chất lượng (`docs/quality/`)** | Kiểm tra sự tồn tại và nội dung của toàn bộ các tệp Markdown trong `docs/delivery/` và `docs/quality/BE_05C_REPORT.md` | Đầy đủ 10/10 tài liệu kỹ thuật, truy vết, nghiệm thu, hướng dẫn cài đặt/sử dụng, nội dung khóa luận và kịch bản bảo vệ. | ✅ **PASS** |
| **10**| **Ghi nhận trung thực các giới hạn & phạm vi P0/P1** | Đối chiếu [SRS_IMPLEMENTATION_TRACEABILITY.md](SRS_IMPLEMENTATION_TRACEABILITY.md) | Phân loại rõ `FR-AUTH-02` (`NOT IMPLEMENTED`), `FR-RPT-05` (`OUT OF SCOPE P1`), `sqlExceptions`/`deadlocks` (`NOT MEASURED`). | ✅ **PASS** |
| **11**| **Quy trình UAT cho Camera vật lý** | Đối chiếu [MANUAL_CAMERA_UAT.md](MANUAL_CAMERA_UAT.md) | Ghi nhận rõ **`MANUAL UAT — NOT TESTED`** trong môi trường tự động hóa kèm 12 kịch bản kiểm thử thiết bị thực địa. | ✅ **DOCUMENTED** |
| **12**| **Trạng thái GitHub Actions CI trên nhánh bàn giao** | PR #8 CI (`41285460717`) đã PASS 4/4 jobs; lỗi giải mã ảnh QR không ổn định trên `main` push runs (`41279361688` và `41285939537`) đã được khắc phục triệt để trong RC-01 (`ECCLevel.M` + `GetGraphic(7)` + `#qrFileScratch` + ZXing `TRY_HARDER` + `decodeQrFileDeterministically` + trạng thái `"ẢNH QR LỖI"`) và xác minh qua 3 lượt chạy toàn bộ bộ Playwright E2E liên tiếp (`retries: 0`). | Đã xác minh PASS 100% tại máy trạm cục bộ; chờ người dùng cho phép `git push` nhánh `fix/rc1-final-stabilization`. | ⏳ **READY FOR PUSH** |

---

## 2. Danh mục Tài liệu Bàn giao Đầy đủ trong Kho Mã nguồn

| STT | Đường dẫn tệp | Nội dung chính |
|:---:|---|---|
| 1 | [docs/delivery/SRS_IMPLEMENTATION_TRACEABILITY.md](SRS_IMPLEMENTATION_TRACEABILITY.md) | Ma trận truy vết toàn diện 54 FR, 18 BR, 18 NFR, 9 CR, 16 UC, 6 BPMN, 14 SD và 25 TC. |
| 2 | [docs/delivery/ARCHITECTURE_AND_DATA_MODEL.md](ARCHITECTURE_AND_DATA_MODEL.md) | Kiến trúc Modular Monolith .NET 10, danh mục Route/API, phân cấp khóa SQL Server, máy trạng thái, mật mã QR và giải trình khác biệt thiết kế. |
| 3 | [docs/delivery/TEST_ACCEPTANCE_SUMMARY.md](TEST_ACCEPTANCE_SUMMARY.md) | Tổng hợp kết quả 9 tầng kiểm thử (166 xUnit + 5 Playwright E2E + 9 bất biến SQL) và cơ sở hiệu năng thực nghiệm. |
| 4 | [docs/delivery/MANUAL_CAMERA_UAT.md](MANUAL_CAMERA_UAT.md) | Quy trình nghiệm thu thủ công 12 kịch bản cho camera điện thoại (Android/iPhone), webcam và đầu đọc 2D USB. |
| 5 | [docs/delivery/INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md) | Hướng dẫn cài đặt môi trường, biến cấu hình, lưu ý giữ ổn định khóa `BGP_QR_AES_KEY_B64`, chạy migration/seed và kiểm thử. |
| 6 | [docs/delivery/USER_MANUAL.md](USER_MANUAL.md) | Hướng dẫn sử dụng chi tiết theo từng vai trò (`GUEST`, `CUSTOMER`, `STAFF`, `MANAGER`/`ADMIN`). |
| 7 | [docs/delivery/THESIS_TECHNICAL_CONTENT.md](THESIS_TECHNICAL_CONTENT.md) | Nội dung học thuật chuẩn hóa theo cấu trúc 5 chương phục vụ thuyết minh báo cáo đề án tốt nghiệp MIS. |
| 8 | [docs/delivery/DEFENSE_DEMO_SCRIPT.md](DEFENSE_DEMO_SCRIPT.md) | Kịch bản trình diễn trực tiếp 14 bước (12–15 phút), phương án dự phòng khẩn cấp và bảng Hỏi–Đáp phản biện. |
| 9 | [docs/delivery/RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) | Danh mục kiểm tra chất lượng bàn giao cuối cùng và khuyến nghị hợp nhất/gắn nhãn phiên bản. |
| 10 | [docs/quality/BE_05C_REPORT.md](../quality/BE_05C_REPORT.md) & [docs/quality/RC1_FINAL_AUDIT_REPORT.md](../quality/RC1_FINAL_AUDIT_REPORT.md) | Báo cáo tổng kết kỹ thuật giai đoạn BE-05C và Báo cáo kiểm toán chuẩn bị phát hành RC-01. |

---

## 3. Khuyến nghị Quy trình Hợp nhất (Merge) và Đóng gói Phiên bản (Release Tag)

Sau khi người dùng rà soát và phê duyệt bản commit cục bộ trên nhánh `fix/rc1-final-stabilization`:
1. **Đẩy nhánh lên GitHub:**
   ```powershell
   git push -u origin fix/rc1-final-stabilization
   ```
2. **Mở Pull Request và chờ 4/4 jobs của GitHub Actions CI báo xanh (`SUCCESS`).**
3. **Hợp nhất (Merge) vào nhánh `main`.**
4. **Đề xuất gắn thẻ phiên bản (Candidate Tag — chỉ thực hiện khi có yêu cầu):**
   - Tên thẻ đề xuất: **`v1.0.0-capstone-rc1`**
   - Ý nghĩa: Bản hoàn thiện đầy đủ mã nguồn, kiểm thử tự động (Unit/Integration/Web/E2E) và hồ sơ tài liệu bảo vệ đồ án tốt nghiệp.
