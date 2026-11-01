# BÁO CÁO TRIỂN KHAI & CHẤT LƯỢNG GIAI ĐOẠN BE-04B
## VẬN HÀNH QUẢN LÝ, CÔNG BỐ LỊCH CHIẾU, ĐỊNH GIÁ GHẾ & BẢNG ĐIỀU KHIỂN DOANH THU

---

### THÔNG TIN TỔNG QUAN

- **Dự án:** Hệ thống quản lý bán vé trực tuyến rạp chiếu phim Beta Giải Phóng (MIS Capstone Prototype).
- **Giai đoạn:** BE-04B — Manager & Admin Operations, Showtime Publishing, Pricing and Sales Dashboard.
- **Workspace:** `D:\Projects\BetaGiaiPhong`
- **GitHub Repository:** `https://github.com/cakhiempn-a11y/beta-giai-phong-ticketing-system`
- **Nhánh phát triển:** `feat/manager-operations` (tạo trực tiếp từ `main` commit `d3ec9fb`).
- **Tài khoản Git & Committer:** `cakhiempn-a11y` (`320607805+cakhiempn-a11y@users.noreply.github.com`).
- **Công nghệ nền tảng:** .NET 10 SDK, ASP.NET Core 10, Entity Framework Core 10.0.14, Microsoft SQL Server, xUnit, Bootstrap 5.

---

### 1. TỔNG QUAN CÁC HẠNG MỤC ĐÃ HOÀN THÀNH

Giai đoạn BE-04B hoàn thiện toàn bộ năng lực vận hành dành cho Người quản lý (Manager) và Quản trị viên (Admin) mà không làm phá vỡ các nghiệp vụ bán vé, thanh toán hoặc soát vé hiện hữu:

1. **Phase 1 — Nền tảng phân quyền & Bảo mật phiên quản lý:**
   - Chính sách phân quyền `RequireManager` bắt buộc vai trò `MANAGER` hoặc `ADMIN`.
   - Cơ chế xác thực phiên động `OnValidatePrincipal`: Trên mỗi request gửi đến hệ thống, cookie xác thực được kiểm tra đối chiếu trực tiếp với database (`db.Users.Status == "ACTIVE"` và vai trò người dùng). Nếu tài khoản bị khóa (`DISABLED`), bị thu hồi quyền hoặc không còn hợp lệ, phiên làm việc lập tức bị hủy bỏ và chuyển hướng đăng xuất/đăng nhập ngay tức thì.
   - Bảo vệ chống CSRF toàn diện trên 100% các endpoint POST quản lý qua `[ValidateAntiForgeryToken]`.

2. **Phase 2 — Quản lý vòng đời phim (Movie Lifecycle Management):**
   - Quản lý trạng thái theo máy trạng thái xác định: `DRAFT` &rarr; `PUBLISHED` &rarr; `ARCHIVED`.
   - Không cho phép xóa cứng dữ liệu (`No hard delete`) để bảo đảm toàn vẹn lịch sử giao dịch và vé đã phát hành.
   - Kiểm tra ràng buộc hợp lệ: Tên phim, thời lượng từ 1 đến 480 phút, độ tuổi (`P`, `K`, `T13`, `T16`, `T18`, `C`), kiểm tra an toàn URL poster (chỉ chấp nhận HTTPS hoặc đường dẫn tương đối an toàn, chặn hoàn toàn giao thức `javascript:` hoặc URL độc hại).
   - **Khóa thời lượng phim:** Nghiêm cấm thay đổi thời lượng của phim khi đang có suất chiếu hiệu lực (chưa hủy và chưa kết thúc) trong lịch chiếu nhằm bảo vệ tính toàn vẹn của thời gian đệm và lịch phòng chiếu.
   - **Tự động đóng suất chiếu khi lưu trữ:** Khi phim chuyển sang trạng thái `ARCHIVED`, hệ thống tự động đóng (`CLOSED`) các suất chiếu tương lai chưa phát sinh vé bán ra và ngăn chặn triệt để việc công bố suất chiếu mới.
   - Ghi vết kiểm toán chi tiết (`AuditLog`) cho mọi thao tác: `MOVIE_CREATED`, `MOVIE_UPDATED`, `MOVIE_PUBLISHED`, `MOVIE_ARCHIVED`.

3. **Phase 3 — Quản lý & Công bố suất chiếu (Showtime Management & Publishing):**
   - Tái sử dụng trọn vẹn domain service `ShowtimePublishingService.PublishAsync` đã được kiểm thử nghiêm ngặt:
     - Giữ khóa cấp ứng dụng độc quyền phòng chiếu `SqlServerLocks.RoomAsync` trong giao dịch Serializable.
     - Kiểm tra xung đột lịch chiếu phòng và khoảng đệm dọn phòng (`bufferMinutes`).
     - Tự động snapshot danh sách ghế bán được (`SellableCapacitySnapshot`) và tạo bản ghi ghế suất chiếu `ShowtimeSeats` ở trạng thái `AVAILABLE`.
     - Bắt buộc định giá dương cho mọi loại ghế hoạt động có trong phòng chiếu.
   - Chuẩn hóa múi giờ: Giao diện nhận giờ địa phương Việt Nam (UTC+7) và chuyển đổi chính xác sang UTC lưu trữ vào database.
   - Tra cứu và lọc đa chiều: Theo phim, theo phòng chiếu, theo ngày chiếu và trạng thái suất chiếu.
   - **Bảo vệ hủy suất chiếu:** Chỉ cho phép hủy (`CANCELLED`) suất chiếu khi chưa có bất kỳ vé nào được phát hành (`tickets_sold == 0`) và không có ghế ở trạng thái `SOLD`. Khi hủy hợp lệ, toàn bộ ghế đang giữ tạm thời (`HELD`) được giải phóng sạch sẽ về `AVAILABLE`.

4. **Phase 4 — Danh mục phòng chiếu & Sơ đồ ghế vật lý (Read-only Reference):**
   - Cung cấp giao diện tra cứu danh mục phòng chiếu, trạng thái hoạt động, tổng số ghế và số ghế khả dụng.
   - Hiển thị trực quan sơ đồ bố trí ghế vật lý theo ma trận hàng/cột, phân loại ghế (`STANDARD`, `VIP`, `SWEETBOX`) và trạng thái kích hoạt của từng ghế.
   - Tuân thủ nguyên tắc chỉ đọc (Read-only) nhằm ngăn chặn việc thay đổi cấu trúc phòng đã đi vào vận hành.

5. **Phase 5 — Báo cáo & Bảng điều khiển doanh thu (Sales & Operations Dashboard):**
   - Tích hợp trực tiếp với view cơ sở dữ liệu `dbo.vw_ShowtimeSales` (`ShowtimeSalesSummary`).
   - Tuân thủ nguyên tắc tài chính: Doanh thu thực tế (`TotalRevenueVnd`) và số lượng vé (`TotalTicketsSold`) chỉ được ghi nhận từ các đơn hàng `PAID` có vé phát hành hợp lệ.
   - **Công thức lấp đầy bình quân có trọng số chuẩn xác:**
     $$\text{WeightedOccupancyPct} = \frac{\text{TotalTicketsSold} \times 100}{\text{TotalSellableCapacity}}$$
     (Không tính trung bình cộng cơ học của các tỷ lệ phần trăm riêng lẻ, loại bỏ hoàn toàn sai số quy mô phòng).
   - Phân tích chi tiết:
     - Doanh thu & số vé theo từng bộ phim.
     - Doanh thu & số vé theo từng phòng chiếu.
     - Biểu đồ xu hướng doanh thu theo ngày (giờ Việt Nam).
     - Top 5 suất chiếu đạt hiệu quả doanh thu cao nhất.
     - Bảng chi tiết doanh thu và tỷ lệ lấp đầy từng suất chiếu có phân trang.
   - Bộ lọc linh hoạt: Lọc theo khoảng ngày (từ ngày - đến ngày), lọc theo phim, lọc theo phòng chiếu.

6. **Phase 6 — Khắc phục tồn đọng kỹ thuật & Tăng cường an toàn Seeder:**
   - Sửa route xem vé demo trong `src/BetaGiaiPhong.Runner/Program.cs`: Trỏ chính xác về `/orders/{res.OrderId}/tickets` (loại bỏ route chết cũ `/tickets/order/{OrderCode}`).
   - Thắt chặt điều kiện bảo vệ Seeder: Nếu phát hiện vi phạm môi trường Production hoặc thiếu cờ opt-in, hệ thống dừng ngay lập tức và trả về mã lỗi `Environment.ExitCode = 1` (Fail-Closed).

7. **Phase 7 — Bộ kiểm thử tự động toàn diện:**
   - Xây dựng mới `tests/BetaGiaiPhong.Tests/WebTests/ManagementWebTests.cs` gồm 12 ca kiểm thử tích hợp & web tests bao phủ toàn diện các khía cạnh phân quyền, vòng đời phim, công bố suất chiếu, sơ đồ phòng và bảng điều khiển doanh thu.
   - Toàn bộ test suite đạt **144/144 tests PASS** (vượt baseline 132 tests ban đầu).

---

### 2. DANH MỤC CÁC TỆP ĐÃ TẠO MỚI VÀ CHỈNH SỬA

| Trạng thái | Đường dẫn tệp | Mô tả chi tiết |
|---|---|---|
| Mới | `src/BetaGiaiPhong.Data/Services/MovieManagementService.cs` | Domain service quản lý vòng đời phim (Tạo, Sửa, Xuất bản, Lưu trữ, Khóa thời lượng, AuditLog) |
| Mới | `src/BetaGiaiPhong.Data/Services/ManagementQueryService.cs` | Query service phục vụ báo cáo doanh thu, lọc suất chiếu, chi tiết sơ đồ ghế và danh mục phòng |
| Mới | `src/BetaGiaiPhong.Web/Controllers/ManagementController.cs` | Controller MVC quản lý vận hành rạp (`/management`), phân quyền `RequireManager` |
| Mới | `src/BetaGiaiPhong.Web/Models/Requests/ManagementRequests.cs` | DTO request tạo/sửa phim, công bố suất chiếu với validation DataAnnotations |
| Mới | `src/BetaGiaiPhong.Web/Models/ViewModels/ManagementViewModels.cs` | ViewModels cho báo cáo doanh thu, danh mục phim, lịch chiếu và sơ đồ phòng |
| Mới | `src/BetaGiaiPhong.Web/Views/Management/Reports.cshtml` | Giao diện Dashboard doanh thu, KPI cards, phân tích phim/phòng/ngày, top suất chiếu |
| Mới | `src/BetaGiaiPhong.Web/Views/Management/Movies.cshtml` | Giao diện danh mục phim: tìm kiếm từ khóa, lọc trạng thái, phân trang |
| Mới | `src/BetaGiaiPhong.Web/Views/Management/MovieCreate.cshtml` | Form nhập thông tin tạo phim mới (DRAFT) với hướng dẫn validation |
| Mới | `src/BetaGiaiPhong.Web/Views/Management/MovieEdit.cshtml` | Form chỉnh sửa phim, khóa input thời lượng nếu có suất chiếu hiệu lực |
| Mới | `src/BetaGiaiPhong.Web/Views/Management/MovieDetail.cshtml` | Chi tiết phim, nút bấm chuyển trạng thái (Publish, Archive) và bảng suất chiếu gắn kèm |
| Mới | `src/BetaGiaiPhong.Web/Views/Management/Showtimes.cshtml` | Quản lý lịch chiếu: lọc phim, phòng, ngày, trạng thái, vé bán, công suất và doanh thu |
| Mới | `src/BetaGiaiPhong.Web/Views/Management/ShowtimeCreate.cshtml` | Form công bố suất chiếu: chọn phim đã xuất bản, phòng hoạt động, giờ VN, giá theo loại ghế |
| Mới | `src/BetaGiaiPhong.Web/Views/Management/ShowtimeDetail.cshtml` | Chi tiết suất chiếu: KPI doanh thu, bảng giá, nút đóng/hủy, sơ đồ ghế thời gian thực |
| Mới | `src/BetaGiaiPhong.Web/Views/Management/Rooms.cshtml` | Danh mục phòng chiếu tham chiếu (Read-only), tổng số ghế, ghế hoạt động |
| Mới | `src/BetaGiaiPhong.Web/Views/Management/RoomDetail.cshtml` | Sơ đồ ma trận ghế vật lý (A..Z, 1..N), phân loại STANDARD/VIP/SWEETBOX |
| Mới | `tests/BetaGiaiPhong.Tests/WebTests/ManagementWebTests.cs` | 12 ca kiểm thử web tích hợp: Auth matrix, Session security, Movie lifecycle, Showtime publishing, Dashboard |
| Sửa | `src/BetaGiaiPhong.Data/ServiceRegistration.cs` | Đăng ký `MovieManagementService` và `ManagementQueryService` vào DI container |
| Sửa | `src/BetaGiaiPhong.Web/Program.cs` | Cấu hình `OnValidatePrincipal` kiểm tra trạng thái tài khoản active & role động |
| Sửa | `src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml` | Thêm menu dropdown "Quản lý Vận hành" cho `MANAGER` và `ADMIN` |
| Sửa | `src/BetaGiaiPhong.Runner/Program.cs` | Sửa route demo `/orders/{OrderId}/tickets` và thắt chặt exit code khi bị chặn |
| Sửa | `README.md` | Cập nhật tài liệu các route `/management`, cập nhật bảng phân quyền và số lượng test (144) |

---

### 3. MA TRẬN PHÂN QUYỀN & BẢO MẬT (PERMISSION MATRIX)

| Phân hệ / Tuyến đường | Phương thức | Anonymous | CUSTOMER | STAFF | MANAGER | ADMIN |
|---|---|:---:|:---:|:---:|:---:|:---:|
| `/management/reports` | GET | 302 Login | 302 AccessDenied | 302 AccessDenied | **200 OK** | **200 OK** |
| `/management/movies` | GET | 302 Login | 302 AccessDenied | 302 AccessDenied | **200 OK** | **200 OK** |
| `/management/movies/create` | GET / POST | 302 Login | 302 AccessDenied | 302 AccessDenied | **200 OK / Redirect** | **200 OK / Redirect** |
| `/management/movies/{id}` | GET | 302 Login | 302 AccessDenied | 302 AccessDenied | **200 OK** | **200 OK** |
| `/management/movies/{id}/edit` | GET / POST | 302 Login | 302 AccessDenied | 302 AccessDenied | **200 OK / Redirect** | **200 OK / Redirect** |
| `/management/movies/{id}/publish` | POST | 302 Login | 302 AccessDenied | 302 AccessDenied | **Redirect** | **Redirect** |
| `/management/movies/{id}/archive` | POST | 302 Login | 302 AccessDenied | 302 AccessDenied | **Redirect** | **Redirect** |
| `/management/showtimes` | GET | 302 Login | 302 AccessDenied | 302 AccessDenied | **200 OK** | **200 OK** |
| `/management/showtimes/create` | GET / POST | 302 Login | 302 AccessDenied | 302 AccessDenied | **200 OK / Redirect** | **200 OK / Redirect** |
| `/management/showtimes/{id}` | GET | 302 Login | 302 AccessDenied | 302 AccessDenied | **200 OK** | **200 OK** |
| `/management/showtimes/{id}/close` | POST | 302 Login | 302 AccessDenied | 302 AccessDenied | **Redirect** | **Redirect** |
| `/management/showtimes/{id}/cancel` | POST | 302 Login | 302 AccessDenied | 302 AccessDenied | **Redirect** | **Redirect** |
| `/management/rooms` | GET | 302 Login | 302 AccessDenied | 302 AccessDenied | **200 OK** | **200 OK** |
| `/management/rooms/{id}` | GET | 302 Login | 302 AccessDenied | 302 AccessDenied | **200 OK** | **200 OK** |

---

### 4. KẾT QUẢ KIỂM ĐỊNH CHẤT LƯỢNG & BẢO MẬT TOÀN DIỆN

1. **Biên dịch giải pháp (Build Verification):**
   ```powershell
   dotnet build .\BetaGiaiPhong.slnx -c Release
   ```
   - **Kết quả:** `Build succeeded. 0 Warning(s), 0 Error(s)`.

2. **Kiểm tra độ trôi mô hình dữ liệu (EF Model Drift Check):**
   ```powershell
   dotnet ef migrations has-pending-model-changes --project .\src\BetaGiaiPhong.Data --startup-project .\src\BetaGiaiPhong.Data
   ```
   - **Kết quả:** `No changes have been made to the model since the last migration.` (Hoàn toàn không có model drift hay phát sinh migration thừa).

3. **Quét lỗ hổng bảo mật các gói phụ thuộc (NuGet Vulnerability Audit):**
   ```powershell
   dotnet list package --vulnerable --include-transitive
   ```
   - **Kết quả:** Không phát hiện bất kỳ lỗ hổng bảo mật nào trong toàn bộ 4 dự án (`BetaGiaiPhong.Data`, `BetaGiaiPhong.Runner`, `BetaGiaiPhong.Web`, `BetaGiaiPhong.Tests`).

4. **Kiểm tra tĩnh cấu trúc lược đồ SQL (Static Schema Check):**
   ```powershell
   python .\scripts\static_schema_checks.py
   ```
   - **Kết quả:** `PASS: 17 tables, 116 columns, 22 FKs; migration/snapshot/code mapping structurally consistent.`

5. **Bộ kiểm thử tự động toàn diện (Automated Test Suite):**
   ```powershell
   dotnet test .\BetaGiaiPhong.slnx --logger "console;verbosity=normal"
   ```
   - **Kết quả:**
     - **Tổng số ca kiểm thử:** **144 tests**.
     - **Trạng thái:** **144 Passed (100%)**, 0 Failed, 0 Skipped.
     - **Thời gian chạy:** ~1.2 phút.

6. **Kiểm tra tính toàn vẹn cơ sở dữ liệu nhiều bảng (Database Multi-Table Invariant Verification):**
   ```powershell
   dotnet run --project .\src\BetaGiaiPhong.Runner -- verify
   ```
   - **Kết quả kiểm tra thực tế:**
     - `Wrong room/seat:` **0 violation(s)**
     - `SOLD seat without PAID order:` **0 violation(s)**
     - `PAID orders missing tickets:` **0 violation(s)**
     - `Order price snapshot mismatch:` **0 violation(s)**
     - `Late payment improperly SUCCESS:` **0 violation(s)**
     - `Dead order still holds seat:` **0 violation(s)**
     - `INTEGRITY PASSED`

---

### 5. KẾT LUẬN & TRẠNG THÁI SẴN SÀNG

Giai đoạn **BE-04B** đã được triển khai hoàn chỉnh, tuân thủ nghiêm ngặt các nguyên tắc bảo mật ASP.NET Core, tính toán tài chính nhất quán dựa trên view `dbo.vw_ShowtimeSales`, bảo vệ toàn vẹn lịch chiếu và vượt qua toàn bộ 144 bài kiểm thử tự động.

Nhánh `feat/manager-operations` đã sẵn sàng để tạo commit cục bộ theo đúng quy định.
