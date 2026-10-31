# BÁO CÁO TRIỂN KHAI, HARDENING & MERGE GATE GIAI ĐOẠN BE-04A
## VẬN HÀNH SOÁT VÉ QR, BẢO VỆ CLIENT, TỰ ĐỘNG XỬ LÝ HẾT HẠN & BẢO TOÀN TÍNH TOÀN VẸN LỊCH CHIẾU

---

### THÔNG TIN TỔNG QUAN

- **Dự án:** Hệ thống quản lý bán vé trực tuyến rạp chiếu phim Beta Giải Phóng (MIS Capstone Prototype).
- **Giai đoạn:** BE-04A — Staff QR Scanner, Ticket Check-in & Automatic Reservation Expiry (Bao gồm Final Hardening, Code Review Remediation & Merge Gate Targeted Fixes).
- **Workspace:** `D:\Projects\BetaGiaiPhong`
- **GitHub Repository:** `https://github.com/cakhiempn-a11y/beta-giai-phong-ticketing-system`
- **Nhánh phát triển:** `feat/staff-checkin-expiry` (tạo từ `main` commit `cd2f7b677c12`).
- **Tài khoản Git & Committer:** `cakhiempn-a11y` (`320607805+cakhiempn-a11y@users.noreply.github.com`).
- **Môi trường & Công nghệ:** .NET 10 SDK, ASP.NET Core 10, Entity Framework Core 10.0.14, Microsoft SQL Server, xUnit, `html5-qrcode` 2.3.8 (vendored cục bộ có LICENSE Apache-2.0).

---

### 1. TỔNG QUAN CÁC HẠNG MỤC MERGE GATE TARGETED FIXES

Sau các vòng rà soát độc lập, đợt Merge Gate đã hoàn thành 100% các yêu cầu làm sạch và bảo vệ hệ thống:

1. **P1 — Bảo toàn tính toàn vẹn lịch chiếu trong Seeder (`seed-checkin-demo`)**:
   - Tệp: `src/BetaGiaiPhong.Runner/Program.cs`.
   - **Loại bỏ việc chèn thực thể trực tiếp:** Chuyển hoàn toàn sang sử dụng domain service chuẩn `ShowtimePublishingService.PublishAsync`.
   - **Thời lượng phim:** Không hardcode 120 phút; tự động lấy `Movie.DurationMin` từ danh mục phim để tính `EndUtc = StartUtc.AddMinutes(movie.DurationMin)`.
   - **Sức chứa snapshot:** Không hardcode 10 ghế; tính toán chính xác từ số lượng ghế hoạt động (`IsActive`) trong phòng chiếu.
   - **Cấu hình giá:** Tự động truy vấn các loại ghế thực tế có trong phòng (`SeatTypeCode`) để tạo `SeatPriceInput` tương ứng, tuân thủ chặt chẽ validation của domain model.
   - **Chống xung đột lịch phòng:** Kiểm tra xung đột lịch chiếu và thời gian đệm `bufferMinutes` (15 phút) qua `SqlServerLocks.RoomAsync`.
   - **Tính lũy kế & Idempotency:** Ưu tiên tìm kiếm suất chiếu hợp lệ sẵn có trong khung giờ soát vé (`now < StartUtc <= now + 25m`) còn ghế trống (`State == SeatStates.Available`) để tái sử dụng; chỉ tạo mới khi chưa có suất chiếu phù hợp.
   - **An toàn môi trường (Fail-Closed):** Bắt buộc opt-in qua cờ `BGP_ALLOW_CHECKIN_DEMO=true` hoặc `BGP_ALLOW_DEMO_SEED=true`. Tự động từ chối và chặn đứng lệnh nếu chuỗi kết nối trỏ vào database Production (`ServerName`, `BetaDb_Prod`, `Production`, v.v.).
   - **Bảo toàn giao dịch:** Nếu một bước tạo demo thất bại, toàn bộ giao dịch được rollback sạch sẽ; không tự ý thay đổi hay ghi đè các suất chiếu đã bán vé.

2. **P1 — Chuẩn hóa hướng dẫn & Script sinh khóa CSPRNG**:
   - Tệp: `README.md`, `scripts/01_prepare_dev_session.ps1`.
   - **Loại bỏ hoàn toàn `Get-Random`:** Thay thế bằng `System.Security.Cryptography.RandomNumberGenerator` tạo 2 khóa 32-byte độc lập chuẩn CSPRNG.
   - **Chống rò rỉ khóa:** Khóa được truyền trực tiếp vào `dotnet user-secrets set` và biến tạm được thu hồi (`Remove-Variable`), không bao giờ in giá trị khóa ra console dưới dạng plain text.
   - **Cảnh báo tính ổn định khóa (Key Stability):** Bổ sung cảnh báo quan trọng `[!CAUTION]` về việc tuyệt đối không xoay khóa `BGP_QR_AES_KEY_B64` trên cơ sở dữ liệu đã có vé phát hành, vì sẽ khiến toàn bộ mã QR cũ không thể giải mã tại quầy soát vé.
   - **Chặn đứng Ephemeral Keys tự động (Fail-Closed):** Script `01_prepare_dev_session.ps1` sẽ dừng lại và hiển thị hướng dẫn chi tiết nếu thiếu khóa trong User Secrets, chỉ cấp khóa tạm thời khi có opt-in tường minh (`$env:BGP_ALLOW_EPHEMERAL_KEYS = 'true'`).
   - **Làm rõ ranh giới Web vs Runner:** Phân định rõ Web project tự động nạp User Secrets trong Development, trong khi CLI/Runner chạy ngoài Web host context cần biến môi trường được script trích xuất đồng bộ.
   - **Bảo toàn chuỗi kết nối:** Không ghi đè biến môi trường `ConnectionStrings__BetaDb` nếu đã được người dùng thiết lập trước đó.

3. **P2 — Giới hạn hiển thị Token QR thô từ Runner**:
   - Mặc định che giấu (mask) token QR thô khi chạy `seed-checkin-demo`.
   - Chỉ xuất token thô khi có cờ `$env:BGP_SHOW_DEMO_QR_TOKEN = 'true'` kèm cảnh báo bảo mật.
   - Hiển thị đường dẫn tương đối và URL động qua biến `BGP_BASE_URL` hoặc `ASPNETCORE_URLS` thay vì hardcode `localhost:5000`.

4. **P2 — Hoàn thiện các kịch bản kiểm thử tranh chấp đồng thời (`ReservationExpiryWorkerTests`)**:
   - Tách `TC_WORKER_06` thành 3 kịch bản độc lập có kiểm soát thời gian chặt chẽ, không nuốt ngoại lệ, không dùng `Thread.Sleep`:
     - **`TC_WORKER_06a`:** Thanh toán hợp lệ được cam kết trước hạn; worker quét sau khi thời hạn trôi qua vẫn bảo toàn nguyên vẹn đơn `PAID`, ghế `SOLD`, vé `ISSUED` và thanh toán `SUCCESS`. Đảm bảo `p.resolved_utc <= o.expires_utc` giữ vững bất biến.
     - **`TC_WORKER_06b`:** Đơn hàng hết hạn được worker quét và cam kết trước; webhook thanh toán muộn `SUCCESS` gửi đến sau đó không thể đảo ngược lịch sử terminal, không phát sinh vé, ghế giữ nguyên `AVAILABLE`, thanh toán chuyển sang `NEEDS_REVIEW`.
     - **`TC_WORKER_06c`:** Chạy đua đồng thời tại ranh giới thời hạn trên hai DbContext riêng biệt tranh chấp khóa cấp ứng dụng SQL Server (`SqlServerLocks.ShowtimeAsync`); xác nhận kết quả nguyên tử 100%, tuyệt đối không có trạng thái lai tạp (zero hybrid states).

5. **P2 — Siết chặt siêu dữ liệu thư viện vendored `html5-qrcode`**:
   - Bổ sung `src/BetaGiaiPhong.Web/wwwroot/lib/html5-qrcode/README.md` (ghi rõ phiên bản 2.3.8, tác giả, link repository).
   - Bổ sung `src/BetaGiaiPhong.Web/wwwroot/lib/html5-qrcode/LICENSE.txt` (giấy phép chính thức Apache License 2.0).

---

### 2. DANH MỤC CÁC TỆP ĐÃ THAY ĐỔI VÀ TẠO MỚI

| Thao tác | Đường dẫn tệp | Nội dung thực hiện |
|---|---|---|
| Mới | `src/BetaGiaiPhong.Web/wwwroot/lib/html5-qrcode/README.md` | Siêu dữ liệu phiên bản 2.3.8 và tác giả thư viện QR scanner |
| Mới | `src/BetaGiaiPhong.Web/wwwroot/lib/html5-qrcode/LICENSE.txt` | Giấy phép bản quyền mã nguồn mở Apache License 2.0 |
| Mới | `src/BetaGiaiPhong.Web/Api/StaffTicketsApiController.cs` | API endpoint `POST /api/staff/tickets/scan` soát vé bảo mật |
| Mới | `src/BetaGiaiPhong.Web/Controllers/StaffCheckInController.cs` | Controller quản lý màn hình soát vé và lịch sử soát vé |
| Mới | `src/BetaGiaiPhong.Web/Models/Requests/ScanTicketRequest.cs` | DTO request kiểm tra hợp lệ dữ liệu soát vé |
| Mới | `src/BetaGiaiPhong.Web/Models/Responses/ScanTicketResponse.cs` | DTO response trả về kết quả soát vé và chi tiết vé |
| Mới | `src/BetaGiaiPhong.Web/Models/ViewModels/StaffCheckInViewModels.cs` | ViewModels cho màn hình soát vé và bảng lịch sử |
| Mới | `src/BetaGiaiPhong.Web/Services/Workers/ReservationExpiryWorker.cs` | BackgroundService định kỳ quét đơn hàng hết hạn giữ ghế |
| Mới | `src/BetaGiaiPhong.Web/Views/StaffCheckIn/Index.cshtml` | Giao diện soát vé: camera, file upload, cờ in-flight, DOM an toàn |
| Mới | `src/BetaGiaiPhong.Web/Views/StaffCheckIn/History.cshtml` | Giao diện tra cứu lịch sử soát vé và nhật ký từ chối |
| Mới | `src/BetaGiaiPhong.Web/wwwroot/lib/html5-qrcode/html5-qrcode.min.js` | Thư viện quét mã QR cục bộ (375 KB, zero runtime CDN dependency) |
| Mới | `scripts/03_seed_checkin_demo.ps1` | Script tiện ích tự động seed suất chiếu và vé demo |
| Sửa | `src/BetaGiaiPhong.Runner/Program.cs` | Nâng cấp `seed-checkin-demo` qua `ShowtimePublishingService`, bảo vệ prod, mask raw token |
| Sửa | `scripts/01_prepare_dev_session.ps1` | Nâng cấp CSPRNG `RandomNumberGenerator`, fail-closed khi thiếu khóa, hỗ trợ Docker/Localhost |
| Sửa | `README.md` | Cập nhật lệnh CSPRNG, hướng dẫn User Secrets, cảnh báo xoay khóa AES và số lượng test (132) |
| Sửa | `tests/BetaGiaiPhong.Tests/IntegrationTests/ReservationExpiryWorkerTests.cs` | Tách `TC_WORKER_06a/b/c`, kiểm thử 3 kịch bản concurrency chặt chẽ |
| Sửa | `tests/BetaGiaiPhong.Tests/IntegrationTests/TestDatabaseFixture.cs` | Loại bỏ lệnh xóa cứng đơn hàng, bảo tồn chứng cứ dữ liệu |
| Sửa | `tests/BetaGiaiPhong.Tests/IntegrationTests/ReportingAndIntegrityTests.cs` | Thêm `TC_INT_02` kiểm tra fixture không xóa đơn hàng |
| Sửa | `tests/BetaGiaiPhong.Tests/WebTests/StaffCheckInWebTests.cs` | Thêm `TC_WEB_STAFF_18` xác minh scanner an toàn DOM và không log token |

---

### 3. KẾT QUẢ KIỂM THỬ TỰ ĐỘNG

Chạy toàn bộ bộ kiểm thử của giải pháp qua lệnh:
```powershell
dotnet test .\BetaGiaiPhong.slnx --logger "console;verbosity=normal"
```

**Tổng kết:**
- **Tổng số ca kiểm thử:** 132
- **Số ca kiểm thử ĐẠT:** 132 (100%)
- **Số ca kiểm thử THẤT BẠI:** 0
- **Số ca kiểm thử BỊ BỎ QUA:** 0
- **Thời gian chạy:** ~1.3 phút

#### Phân bổ các nhóm kiểm thử chính trong BE-04A:

1. **`StaffCheckInWebTests` (18 tests - 100% PASS):**
   - Xác thực & Phân quyền (`TC_WEB_STAFF_01` -> `06`): Chặn Unauthenticated (401/302), chặn `CUSTOMER` (403), cấp quyền `STAFF`/`MANAGER`/`ADMIN` (200).
   - Bảo mật & Dữ liệu (`TC_WEB_STAFF_07`, `08`, `15`, `18`): Chặn thiếu CSRF (400), payload rỗng (400), không để lộ token thô/khóa/hash trong JSON response, client không log token và render DOM an toàn bằng `textContent`.
   - Kết quả soát vé (`TC_WEB_STAFF_09` -> `13`): `ACCEPTED` (200), `ALREADY_USED` (409), `WRONG_SHOWTIME` (422), `OUTSIDE_WINDOW` (422), `NOT_FOUND` (404 kèm AuditLog).
   - Chạy đua & Luồng thực tế (`TC_WEB_STAFF_14`, `16`, `17`): Quét song song cùng 1 vé (1 accepted, 1 conflict), luồng E2E tạo vé -> xuất PNG -> decode pure C# -> scan staff API, giao diện tra cứu lịch sử hợp nhất.

2. **`ReservationExpiryWorkerTests` (9 tests - 100% PASS):**
   - `TC_WORKER_01`: Cấu hình tắt worker `BGP_ENABLE_EXPIRY_WORKER=false` -> Không thực thi.
   - `TC_WORKER_02`: Quét và thu hồi các đơn hàng quá hạn giữ ghế -> Đổi thành `EXPIRED`, giải phóng ghế về `AVAILABLE`, ghi `dbo.AuditLogs`.
   - `TC_WORKER_03`: Bảo toàn các đơn hàng còn trong hạn 5 phút giữ ghế -> Giữ `PENDING_PAYMENT` và ghế `HELD`.
   - `TC_WORKER_04`: Bảo toàn toàn diện 4 trạng thái terminal (`PAID`, `CANCELLED`, `FAILED`, `NEEDS_REVIEW`) -> Ghế và vé không bị can thiệp.
   - `TC_WORKER_05`: Tuân thủ giới hạn phân lô `batchSize` khi xử lý hàng loạt.
   - `TC_WORKER_06a`: Thanh toán cam kết trước hạn; worker chạy sau đó bảo toàn đơn `PAID` và ghế `SOLD`.
   - `TC_WORKER_06b`: Worker thu hồi hết hạn trước; webhook thanh toán muộn không phát sinh vé, chuyển `NEEDS_REVIEW`.
   - `TC_WORKER_06c`: Chạy đua trực tiếp tại ranh giới thời hạn trên hai DbContext độc lập với khóa SQL Server -> Kết quả nguyên tử 100%, 0 trạng thái lai tạp.
   - `TC_WORKER_07`: Vòng đời thực tế của `IHostedService` với `PeriodicTimer` (kích hoạt quét sau interval và shutdown êm dịu).

3. **`ReportingAndIntegrityTests` (3 tests - 100% PASS):**
   - `TC_REP_01`: View `dbo.vw_ShowtimeSales` tính toán đúng doanh thu và tỷ lệ lấp đầy.
   - `TC_INT_01`: 6 bất biến toàn vẹn đa bảng trên cơ sở dữ liệu -> 0 vi phạm.
   - `TC_INT_02`: `TestDatabaseFixture` không xóa đơn hàng bất thường.

---

### 4. KẾT QUẢ KIỂM TRA BẢO MẬT & TOÀN VẸN CƠ SỞ DỮ LIỆU

1. **Biên dịch Release:**
   ```powershell
   dotnet build .\BetaGiaiPhong.slnx -c Release
   ```
   -> **Thành công:** 0 Warning(s), 0 Error(s).

2. **Kiểm tra thay đổi Schema EF Core:**
   ```powershell
   dotnet ef migrations has-pending-model-changes --project .\src\BetaGiaiPhong.Data --startup-project .\src\BetaGiaiPhong.Data
   ```
   -> **Thành công:** "No changes have been made to the model since the last migration." (0 pending changes).

3. **Quét lỗ hổng bảo mật NuGet:**
   ```powershell
   dotnet list package --vulnerable --include-transitive
   ```
   -> **Thành công:** 0 vulnerable packages across all 4 projects.

4. **Kiểm tra tĩnh cấu trúc Schema:**
   ```powershell
   python .\scripts\static_schema_checks.py
   ```
   -> **Thành công:** PASS 17 tables, 116 columns, 22 FKs; migration/snapshot/code mapping structurally consistent.

5. **Xác minh toàn vẹn đa bảng qua Runner:**
   ```powershell
   cmd.exe /c "set ConnectionStrings__BetaDb=Server=localhost;Database=BetaGiaiPhong_Dev;Integrated Security=True;TrustServerCertificate=True;& dotnet run --project .\src\BetaGiaiPhong.Runner -- verify"
   ```
   -> **Thành công:**
   ```text
   Wrong room/seat: 0 violation(s)
   SOLD seat without PAID order: 0 violation(s)
   PAID orders missing tickets: 0 violation(s)
   Order price snapshot mismatch: 0 violation(s)
   Late payment improperly SUCCESS: 0 violation(s)
   Dead order still holds seat: 0 violation(s)
   INTEGRITY PASSED (static consistency checks; run race test separately).
   ```

6. **Kiểm tra tính lũy kế của Seeder (`seed-checkin-demo`):**
   - Lần 1: Tạo mới Showtime 2 qua `ShowtimePublishingService`, cấp ghế A1, phát hành vé, che giấu token thô.
   - Lần 2: Tái sử dụng Showtime 2 sẵn có, cấp ghế A2, bảo toàn lịch phòng và không tạo trùng lịch.
   - Khi bật `BGP_SHOW_DEMO_QR_TOKEN=true`: Hiển thị token thô an toàn kèm cảnh báo rõ ràng.

---

### 5. KẾT LUẬN & ĐÁNH GIÁ TRẠNG THÁI MERGE

Toàn bộ các yêu cầu của giai đoạn **BE-04A**, đợt rà soát làm cứng mã nguồn (Final Hardening) và đợt cổng kiểm duyệt Merge Gate Targeted Fixes đã được hoàn thành trọn vẹn và kiểm chứng thực tế:
- 132/132 ca kiểm thử tự động đạt 100% PASS.
- 0 lỗ hổng bảo mật, 0 cảnh báo biên dịch, 0 pending EF migrations.
- 6 bất biến toàn vẹn đa bảng đạt 0 vi phạm trên cả môi trường Dev và Test.
- Seeder tuân thủ nghiêm ngặt domain logic xuất bản suất chiếu và bảo vệ môi trường Production.
- Hướng dẫn sinh khóa và script session bảo mật 100% bằng CSPRNG, loại bỏ hoàn toàn `Get-Random`.

**ĐÁNH GIÁ:** **READY TO MERGE** (Sẵn sàng hợp nhất vào nhánh `main`).
*(Cam kết: Không thực hiện lệnh git push hay git merge khi chưa có sự cho phép rõ ràng từ người dùng).*
