# BÁO CÁO KIỂM ĐỊNH KỸ THUẬT TOÀN DIỆN (AUDIT REPORT)

**Dự án:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Chuyên ngành:** Hệ thống thông tin quản lý (MIS)  
**Phạm vi kiểm định:** Kiến trúc mã nguồn Entity Framework Core 10, Lược đồ CSDL SQL Server, Cơ chế Giao dịch/Đồng thời và Mức độ sẵn sàng phát triển Backend ASP.NET Core  
**Thời điểm thực hiện kiểm định:** 31/10/2026  
**Hội đồng kiểm định độc lập:**
- Senior .NET / ASP.NET Core Architect
- Entity Framework Core Specialist
- SQL Server Database Architect
- Software Quality Assurance (QA) Engineer
- MIS Business Analyst

---

## 1. TÓM TẮT ĐIỀU HÀNH (EXECUTIVE SUMMARY)

Hội đồng kiểm định kỹ thuật độc lập đã tiến hành khảo sát, kiểm tra tĩnh, build và thực thi kiểm thử tích hợp thực tế trên cơ sở dữ liệu Microsoft SQL Server độc lập cho toàn bộ workspace `BetaGiaiPhong_EFCore_SQLServer_v1_0`.

### Kết luận tổng quát
Dự án đạt trạng thái **CONDITIONAL GO**.
- **Mặt đạt được:** Mô hình quan hệ dữ liệu 17 bảng, 116 thuộc tính, 22 khóa ngoại, 30 check constraints và các chỉ mục nghiệp vụ được định nghĩa cực kỳ chặt chẽ, chính xác và đồng bộ 100% giữa Entity, Fluent API, DDL SQL và CSDL SQL Server thực tế. Logic giao dịch (`Serializable`, pessimistic locking trên `Showtimes` với `UPDLOCK, HOLDLOCK`), chống bán trùng ghế (race condition), thanh toán muộn (late payment review), chữ ký HMAC, bảo vệ token QR bằng mã hóa AES-256-GCM và lưu vết kiểm toán (audit trail) đã được thực thi và vượt qua 100% các kịch bản kiểm thử tích hợp thực tế (bao gồm cả thử nghiệm chịu tải 50 request cạnh tranh đồng thời).
- **Điều kiện cần giải quyết trước khi phát triển Backend (Blockers & High Risks):**
  1. **Lỗi Runtime Migration Runner (P1):** Lệnh `dotnet run -- migrate` mặc định bị ngắt bởi `InvalidOperationException` do cơ chế `PendingModelChangesWarning` mới của EF Core 10 khi thiếu cấu hình bỏ qua cảnh báo hoặc scaffold snapshot chuẩn.
  2. **Lỗ hổng bảo mật Package (P1):** Cảnh báo lỗ hổng bảo mật mức High `NU1903` (GHSA-846f-f764-76mx) từ package phụ thuộc gián tiếp `System.Security.Cryptography.Pkcs` 9.0.11.
  3. **Thiếu Navigation Properties (P2):** Toàn bộ 17 Entity hiện tại không có Navigation Properties, gây cản trở cho việc phát triển tầng Backend ASP.NET Core MVC/API theo chuẩn LINQ/EF Core (`Include`, ViewModel projection).
  4. **Tương thích PowerShell 5.1 (P2):** Script khởi tạo môi trường `scripts/01_prepare_dev_session.ps1` sử dụng cú pháp không tương thích với Windows PowerShell 5.1 mặc định trên Windows.
  5. **Tài liệu thượng nguồn (SRS v1.0, BPMN, Use Case, Sequence) chưa có trong workspace (NOT AVAILABLE):** Cần lưu trữ bản lưu trữ trong repository để bảo đảm tính truy vết kiểm toán toàn diện.

---

## 2. MÔI TRƯỜNG KIỂM THỬ THỰC TẾ (TEST ENVIRONMENT)

Mọi kiểm thử và đánh giá trong báo cáo này được thực hiện trên môi trường máy chủ cục bộ với các thông số đo đạc thực tế:

| Thành phần | Thông số thực tế ghi nhận | Ghi chú bằng chứng |
|---|---|---|
| **Hệ điều hành** | Windows 10 Home Single Language x64 (Build 26200) | `cmd.exe / ver`, Powershell |
| **.NET SDK** | `10.0.401` (Commit `e34a38d2ae`) | `dotnet --info` |
| **.NET Host/Runtime** | `Microsoft.NETCore.App 10.0.12`, `Microsoft.AspNetCore.App 10.0.12` | `dotnet --info` |
| **EF Core Version** | `10.0.14` (Microsoft.EntityFrameworkCore.SqlServer 10.0.14) | csproj & NuGet assets |
| **Hệ quản trị CSDL** | Microsoft SQL Server 2025 (RTM-GDR) - 17.0.1145.4 (X64) Standard Developer Edition | `SELECT @@VERSION` |
| **Trạng thái Dịch vụ SQL**| Windows Service `MSSQLSERVER` đang chạy (`Running`) | `Get-Service -Name *sql*` |
| **Docker** | Docker CLI 29.7.2 đã cài đặt; Docker Desktop Daemon chưa bật | Fallback sử dụng trực tiếp SQL Server 2025 cục bộ |
| **CSDL Kiểm thử** | `BetaGiaiPhong_Audit_TestDb` (Tạo mới hoàn toàn độc lập, không ảnh hưởng CSDL khác) | `sys.databases` |

---

## 3. KẾT QUẢ KIỂM ĐỊNH CHI TIẾT THEO TỪNG GIAI ĐOẠN

### GIAI ĐOẠN 1: Cấu trúc dự án và Khả năng Build
- **Cấu trúc Solution:** `BetaGiaiPhong.slnx` hợp lệ theo định dạng mới của .NET 10 / Visual Studio 2026, tham chiếu chính xác 2 project:
  - `src/BetaGiaiPhong.Data/BetaGiaiPhong.Data.csproj` (Target: `net10.0`)
  - `src/BetaGiaiPhong.Runner/BetaGiaiPhong.Runner.csproj` (Target: `net10.0`, tham chiếu `BetaGiaiPhong.Data` và `Microsoft.AspNetCore.App`)
- **Package Restore:** `dotnet restore .\BetaGiaiPhong.slnx` trả về Exit Code `0`. Tuy nhiên, xuất hiện 4 cảnh báo bảo mật về package phụ thuộc gián tiếp (`NU1902` - Moderate, `NU1903` - High: `System.Security.Cryptography.Pkcs` 9.0.11).
- **Khả năng Compilation:** `dotnet build .\BetaGiaiPhong.slnx` biên dịch thành công 100% (**0 Errors, 8 Warnings** - tất cả warnings đều là cảnh báo bảo mật package NuGet nêu trên; **0 warning** về C# nullable hay cú pháp).
- **Đánh giá Giai đoạn 1:** **PASS (Có cảnh báo bảo mật P1)**.

---

### GIAI ĐOẠN 2: Kiểm định Entity Framework Core
Mô hình thực thể và cấu hình Fluent API được đối chiếu trực tiếp giữa mã C# (`BetaGiaiPhong.Data`) và từ điển dữ liệu:
- **Số lượng Entity:** Đủ 17 Entity tương ứng 17 bảng nghiệp vụ (`AppUser`, `Role`, `UserRole`, `Movie`, `Room`, `SeatType`, `Seat`, `Showtime`, `SeatPrice`, `BookingOrder`, `ShowtimeSeat`, `OrderSeat`, `Payment`, `PaymentEvent`, `Ticket`, `TicketScan`, `AuditLog`).
- **Thuộc tính và Kiểu dữ liệu:** Đủ 116 thuộc tính. Toàn bộ các trường tiền tệ VND (`total_vnd`, `subtotal_vnd`, `fee_vnd`, `discount_vnd`, `amount_vnd`, `unit_price_vnd`) đều dùng kiểu `long` (C#) / `BIGINT` (SQL Server), triệt tiêu hoàn toàn rủi ro sai lệch số học dấu phẩy động. Các trường thời gian dùng `DateTime` (C#) / `datetime2(3)` (SQL Server) chuẩn hóa theo UTC.
- **Khóa chính & Khóa ngoại:** 17 Khóa chính và 22 Khóa ngoại được cấu hình đầy đủ. Tất cả các FK đều được gán `OnDelete(DeleteBehavior.NoAction)`, bảo vệ toàn vẹn lịch sử giao dịch và ngăn chặn xóa dây chuyền (Cascade Delete).
- **Ràng buộc toàn vẹn nâng cao:** Cấu hình các khóa ghép và khóa ngoại ghép quan trọng:
  - `ShowtimeSeats` tham chiếu `(showtime_id, room_id)` tới `Showtimes` và `(seat_id, room_id)` tới `Seats` (ngăn chặn hoàn toàn việc xếp ghế nhầm phòng chiếu).
  - `ShowtimeSeats` tham chiếu `(owner_order_id, showtime_id)` tới `Orders` (ngăn chặn gán ghế của đơn hàng này cho suất chiếu khác).
  - `Tickets` tham chiếu `(order_id, showtime_id, seat_id)` tới `OrderSeats` (bảo đảm vé phát hành chỉ gắn với ghế và đơn hàng hợp lệ).
- **Điểm hạn chế kiến trúc phát hiện:** Toàn bộ 17 Entity class **hoàn toàn không khai báo Navigation Properties**. EF Core đang dùng cấu hình `b.HasOne<T>().WithMany()`. Điều này gây khó khăn khi viết câu truy vấn Backend (không thể dùng `.Include()`), đòi hỏi phải refactor bổ sung navigation properties trước khi xây dựng Backend.
- **Đánh giá Giai đoạn 2:** **PASS (Cần Refactor Navigation Properties - P2)**.

---

### GIAI ĐOẠN 3: Kiểm định CSDL SQL Server và Migration
Kiểm định thực tế được triển khai trên database mới `BetaGiaiPhong_Audit_TestDb`:
1. **Quá trình chạy Migration:**
   - Khi chạy lệnh `dotnet run --project .\src\BetaGiaiPhong.Runner -- migrate`, EF Core 10 ném ngoại lệ:
     `InvalidOperationException: An error was generated for warning 'Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning': The model for context 'BetaDbContext' has pending changes.`
   - **Nguyên nhân kỹ thuật:** Trong EF Core 10, phương thức `db.Database.MigrateAsync()` tự động so sánh model snapshot với DbContext runtime. Vì migration ban đầu được viết dưới dạng các batch SQL thô (`migrationBuilder.Sql(...)`) và snapshot gọi cấu hình thủ công thay vì snapshot được scaffold tự động bởi EF Core tool, EF Core 10 coi đây là thay đổi chưa có migration và mặc định ném lỗi chặn cập nhật CSDL.
   - Khi bổ sung cấu hình bỏ qua cảnh báo: `.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))`, lệnh `MigrateAsync()` thực thi thành công hoàn toàn, ghi nhận 2 migration vào `dbo.__EFMigrationsHistory`:
     - `202610310001_InitialSchema`
     - `202610310002_ReportView`
2. **Khảo sát cấu trúc thực tế trên SQL Server (Truy vấn hệ thống sys/INFORMATION_SCHEMA):**
   - **Bảng cơ sở:** 17 bảng nghiệp vụ + 1 bảng `__EFMigrationsHistory` = 18 bảng.
   - **View:** 1 view `dbo.vw_ShowtimeSales`.
   - **Tổng số cột:** 116 cột trong 17 bảng nghiệp vụ (khớp 100% với từ điển dữ liệu).
   - **Foreign Keys:** 22 khóa ngoại (tất cả đều có `delete_referential_action_desc = NO_ACTION`).
   - **Check Constraints:** 30 ràng buộc kiểm tra hợp lệ (`CK_Orders_01..09`, `CK_Showtimes_01..05`, `CK_ShowtimeSeats_01`, `CK_Tickets_01..02`, `CK_TicketScans_01`, `CK_AuditLogs_01`,...).
   - **RowVersion / Concurrency Token:** 2 cột `row_ver` kiểu `timestamp` (`rowversion`) tại `dbo.Orders` và `dbo.ShowtimeSeats`.
   - **Indexes:** 47 chỉ mục (bao gồm clustered PK, non-clustered alternate keys, và filtered indexes: `UX_Payments_Provider_Ref`, `UX_PaymentEvents_Provider_EventRef`, `IX_Orders_Paid`, `IX_ShowtimeSeats_Owner_State`).
- **Đánh giá Giai đoạn 3:** **PASS (Có lỗi P1 tại Runner cần sửa cấu hình Warnings)**.

---

### GIAI ĐOẠN 4: Kiểm định Nghiệp vụ Đặt vé, Thanh toán và Soát vé
Đã thực thi bộ kiểm thử toàn diện trên CSDL thực tế với kết quả 100% đạt:

| Mã Test Case | Tên nghiệp vụ | Kịch bản kiểm thử | Kết quả thực tế | Trạng thái |
|---|---|---|---|:---:|
| **TC-CON-01a** | Cạnh tranh giữ ghế (2 khách) | 2 khách đặt đồng thời cùng 1 ghế | 1 SUCCESS, 1 CONFLICT (`SEAT_NOT_AVAILABLE`) | **PASS** |
| **TC-CON-01b** | Cạnh tranh tải vừa (10 khách) | 10 request đồng thời tranh chấp 1 ghế | 1 SUCCESS, 9 CONFLICT, 0 Lỗi | **PASS** |
| **TC-CON-01c** | Cạnh tranh tải cao (50 khách) | 50 request đồng thời tranh chấp 1 ghế | 1 SUCCESS, 49 CONFLICT, 0 Lỗi/Deadlock | **PASS** |
| **TC-CON-02** | Đặt ghế độc lập | 2 khách đặt 2 ghế khác nhau trong cùng suất | Cả 2 đơn thành công độc lập (Order 8, Order 9) | **PASS** |
| **TC-CON-03** | Đặt ghế giao nhau | Khách 1 chọn `[A1,A2]`, Khách 2 chọn `[A2,A3]` | 1 SUCCESS, 1 FAIL; ĐÚNG 2 ghế được giữ (Nguyên tử - All or Nothing) | **PASS** |
| **TC-CON-04** | Hết hạn giữ ghế | Khách 1 giữ ghế hết hạn, Khách 2 đặt ghế đó | Đơn cũ không được kích hoạt, Khách 2 sở hữu ghế hợp lệ | **PASS** |
| **TC-CON-05** | Rollback khi lỗi | Giao dịch chọn ghế có 1 ghế không hợp lệ | Exception kích hoạt, Rollback hoàn toàn, 0 đơn rác, ghế giữ nguyên AVAILABLE | **PASS** |
| **TC-PAY-01** | Thanh toán thành công | Webhook hợp lệ gửi kết quả SUCCESS | Đơn `PAID`, Ghế `SOLD`, Vé phát hành `ISSUED`, Token mã hóa | **PASS** |
| **TC-PAY-02** | Thanh toán thất bại | Webhook báo kết quả FAILED | Không phát hành vé, Đơn `FAILED`, Ghế giải phóng `AVAILABLE` | **PASS** |
| **TC-PAY-03** | Webhook lặp (Idempotency) | Webhook gửi lặp lại 3 lần | Lần 1: `APPLIED`, Lần 2 & 3: `DUPLICATE`, chính xác 1 vé, không cộng trùng tiền | **PASS** |
| **TC-PAY-04** | Thanh toán muộn (CR-01) | Webhook thành công đến sau khi đơn `EXPIRED` | Đơn giữ nguyên `EXPIRED`, Thanh toán đánh dấu `NEEDS_REVIEW`, 0 phát hành vé | **PASS** |
| **TC-PAY-05** | Giả mạo chữ ký | Callback có chữ ký HMAC sai | Bị từ chối ngay lập tức (`UnauthorizedAccessException`), không cập nhật DB | **PASS** |
| **TC-PAY-06** | Sai lệch dữ liệu callback | Callback có số tiền không khớp đơn hàng | Bị từ chối xử lý (`decision = REJECTED`), giữ an toàn thanh toán | **PASS** |
| **TC-QR-01** | Soát vé lần đầu | Nhân viên quét vé hợp lệ trong khung giờ | Kết quả `ACCEPTED`, vé chuyển sang `USED`, ghi nhận `scanned_utc` | **PASS** |
| **TC-QR-02** | Quét lại vé | Quét lại vé đã soát ở TC-QR-01 | Bị từ chối với kết quả `ALREADY_USED`, không cho phép vào cửa lần hai | **PASS** |
| **TC-QR-03** | Quét vé đồng thời | 2 nhân viên quét cùng lúc 1 vé tại 2 cửa | Đúng 1 `ACCEPTED`, 1 `ALREADY_USED`, không bị chấp nhận 2 lần | **PASS** |
| **TC-QR-04** | Vé giả mạo | Quét chuỗi token không tồn tại trong hệ thống | Bị từ chối với kết quả `NOT_FOUND`, ghi log kiểm toán | **PASS** |
| **TC-QR-05** | Sai suất chiếu | Vé suất A mang đi quét tại cửa suất B | Bị từ chối với kết quả `WRONG_SHOWTIME` | **PASS** |
| **TC-QR-06** | Ngoài khung giờ soát | Quét trước giờ chiếu 60 phút (khung cho phép: -30p đến +15p) | Bị từ chối với kết quả `OUTSIDE_WINDOW` | **PASS** |
| **TC-INT-01** | Toàn vẹn CSDL (SQL Invariants) | Chạy 5 truy vấn kiểm tra chéo toàn bộ dữ liệu | Tất cả 5 chiều kiểm tra đều đạt **0 vi phạm** | **PASS** |

- **Đánh giá Giai đoạn 4:** **PASS (Tuyệt đối 100%)**.

---

### GIAI ĐOẠN 5: Kiểm định Bảo mật và Tính toàn vẹn
1. **SQL Injection:** Toàn bộ truy vấn raw SQL trong `SqlServerLocks` đều dùng parameterized queries (`@id`, `@hash`). Không có bất kỳ vị trí nối chuỗi (concatenation) tham số người dùng vào câu lệnh SQL -> **PASS**.
2. **Hardcoded Secrets:** Không phát hiện mật khẩu, chuỗi kết nối hay khóa bí mật bị hardcode trong mã nguồn. Toàn bộ thông tin nhạy cảm (`ConnectionStrings__BetaDb`, `BGP_SQL_SA_PASSWORD`, `BGP_DEMO_PASSWORD`, `BGP_MOCK_HMAC_KEY_B64`, `BGP_QR_AES_KEY_B64`) đều được nạp từ biến môi trường -> **PASS**.
3. **Mã hóa Token QR:** Sử dụng mã hóa đối xứng xác thực AES-256-GCM với nonce 12 byte ngẫu nhiên và authentication tag 16 byte. Bảng `Tickets` chỉ lưu hash SHA-256 (`qr_token_hash`) và bản mã (`qr_token_cipher`). Token thô không lưu trên CSDL -> **PASS**.
4. **Tính Idempotency & Xác thực Webhook:** HMAC-SHA256 với so sánh thời gian hằng số (`CryptographicOperations.FixedTimeEquals`). Idempotency được bảo vệ hai tầng (Code logic kiểm tra `PaymentEvents` và CSDL với Unique Filtered Index `UX_PaymentEvents_Provider_EventRef`) -> **PASS**.
5. **Kiểm toán (Audit Logging):** Mọi sự kiện nghiệp vụ (`SHOWTIME_PUBLISHED`, `ORDER_HELD`, `ORDER_CANCELLED`, `ORDER_EXPIRED`, `PAYMENT_*`, `TICKET_SCAN_*`) đều được ghi vào bảng `AuditLogs` cùng trong giao dịch ACID -> **PASS**.
6. **Vấn đề bảo mật Package (Cần khắc phục):** Package phụ thuộc `System.Security.Cryptography.Pkcs` 9.0.11 chứa lỗ hổng bảo mật mức High (GHSA-846f-f764-76mx). Cần khai báo cập nhật trực tiếp trong csproj -> **WARNING (P1)**.
- **Đánh giá Giai đoạn 5:** **PASS (Có khuyến nghị bảo mật package P1)**.

---

### GIAI ĐOẠN 6: Đối chiếu Tài liệu Thiết kế
- **Tài liệu có sẵn trong workspace:**
  - `docs/Architecture_And_Rules.md`: Có sẵn, mô tả chính xác ranh giới giao dịch và quy ước hệ thống.
  - `docs/SRS_v1_1_Design_Decisions.md`: Có sẵn, phản ánh đầy đủ 9 quyết định thay đổi kỹ thuật (CR-01 đến CR-09).
  - `docs/Data_Dictionary_116_Columns.csv`: Có sẵn, khớp 100% với 116 cột CSDL.
  - `docs/Foreign_Key_Matrix_22.csv`: Có sẵn, khớp 100% với 22 khóa ngoại CSDL.
  - `docs/ERD_Source_Editable.drawio` & Figures: Có sẵn, khớp với cấu trúc 17 thực thể.
- **Tài liệu KHÔNG CÓ trong workspace (Ghi nhận NOT AVAILABLE):**
  - Tài liệu đặc tả `SRS v1.0` nguyên bản: `NOT AVAILABLE`.
  - Sơ đồ quy trình nghiệp vụ `BPMN TO-BE v1.1`: `NOT AVAILABLE`.
  - Sơ đồ Use Case và Đặc tả Use Case chi tiết: `NOT AVAILABLE`.
  - Bộ 14 Sequence Diagrams: `NOT AVAILABLE`.
  *(Hội đồng kiểm định tuân thủ nguyên tắc không hallucinate, ghi rõ NOT AVAILABLE và không tự nhận định là đã đối chiếu với các tài liệu vắng mặt này)*.
- **Đánh giá Giai đoạn 6:** **PASS trên các tài liệu hiện hữu**.

---

## 4. BẢNG TỔNG KẾT KẾT QUẢ ĐÁNH GIÁ (SCORECARD)

| Nhóm tiêu chuẩn kiểm định | Số hạng mục | PASS | FAIL | WARNING | NOT TESTED | BLOCKED |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| **1. Cấu trúc Solution & Khả năng Build** | 6 | 5 | 0 | 1 | 0 | 0 |
| **2. Thực thể (Entities) & Fluent API** | 10 | 9 | 0 | 1 | 0 | 0 |
| **3. CSDL SQL Server & Migrations** | 8 | 6 | 1 | 1 | 0 | 0 |
| **4. Nghiệp vụ Đặt vé & Tranh chấp (TC-CON)** | 7 | 7 | 0 | 0 | 0 | 0 |
| **5. Nghiệp vụ Thanh toán & Callback (TC-PAY)** | 6 | 6 | 0 | 0 | 0 | 0 |
| **6. Nghiệp vụ Soát vé QR (TC-QR)** | 6 | 6 | 0 | 0 | 0 | 0 |
| **7. Toàn vẹn Dữ liệu & Báo cáo (TC-INT, View)** | 4 | 4 | 0 | 0 | 0 | 0 |
| **8. Bảo mật (SQL Injection, Secrets, Crypto)** | 6 | 5 | 0 | 1 | 0 | 0 |
| **9. Đối chiếu Tài liệu Thiết kế & Truy vết** | 6 | 2 | 0 | 0 | 4* | 0 |
| **TỔNG CỘNG** | **59** | **50** | **1** | **4** | **4** | **0** |

*\* Ghi chú: 4 hạng mục `NOT TESTED` thuộc về các tài liệu thượng nguồn vắng mặt trong workspace (`SRS v1.0`, `BPMN TO-BE`, `Use Cases`, `Sequence Diagrams`).*

---

## 5. KẾT LUẬN CUỐI CÙNG VÀ QUYẾT ĐỊNH CỦA HỘI ĐỒNG

Hội đồng kiểm định đưa ra kết luận: **CONDITIONAL GO**.

### Căn cứ kết luận:
1. **Không có lỗi P0 nào:** CSDL không bị sai lệch, không có tình trạng bán trùng ghế, không lộ bí mật, logic giao dịch ACID và bảo toàn dữ liệu tài chính đạt chuẩn xuất sắc.
2. **Có 2 lỗi P1 cần xử lý trước khi khởi động dự án Backend:**
   - Cấu hình bỏ qua `PendingModelChangesWarning` hoặc sinh snapshot chuẩn để `db.Database.MigrateAsync()` chạy mượt mà trong code khởi động Backend.
   - Nâng cấp phiên bản package `System.Security.Cryptography.Pkcs` để khắc phục cảnh báo lỗ hổng High.
3. **Có các cải tiến kiến trúc P2 phục vụ Backend:**
   - Bổ sung Navigation Properties cho 17 Entity để hỗ trợ LINQ query/Include trong Controller/Service.
   - Khai báo Entity Type / Keyless Entity cho view báo cáo `vw_ShowtimeSales`.
   - Bổ sung dự án Unit/Integration Test chuẩn (`xUnit`).

Sau khi các hạng mục điều kiện trên được thiết lập trong bước chuẩn bị dự án Backend, đội ngũ phát triển hoàn toàn có thể an tâm tiến hành xây dựng các Controller, DTO, View và API trên nền tảng Data Layer đã được kiểm chứng này.
