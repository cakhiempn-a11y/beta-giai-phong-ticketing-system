# BÁO CÁO BÀN GIAO GIAI ĐOẠN 2 (BE/DATA-P2)
## Lược Đồ Phim Thật Có Nguồn Xác Minh, Đường Ống Ảnh Đa Kích Thước Cục Bộ & Dịch Vụ Đồng Bộ Danh Mục (`BE_DATA_P2_IMPLEMENTATION_REPORT.md`)

**Dự án:** Hệ thống Quản lý Bán vé Trực tuyến tại Rạp chiếu phim Beta Giải Phóng  
**Giai đoạn:** `BE/DATA-P2` — Real Movie Provenance Schema, Local Multi-Size Media Pipeline & Grounded Catalog APIs  
**Nhánh thực hiện:** `ui/p1-cinema-design-system-dual-shell`  
**Tài liệu quy chiếu:** [`NETFLIX_UI_MASTER_PLAN.md`](NETFLIX_UI_MASTER_PLAN.md) • [`REAL_MOVIE_CONTENT_PLAN.md`](REAL_MOVIE_CONTENT_PLAN.md) • [`UI_P1_IMPLEMENTATION_REPORT.md`](UI_P1_IMPLEMENTATION_REPORT.md)

---

## 1. Mục Tiêu & Ranh Giới Kiến Trúc Giai Đoạn `BE/DATA-P2`

Giai đoạn `BE/DATA-P2` hiện thực hóa toàn bộ nền tảng dữ liệu phim thật và nguồn gốc pháp lý cho trải nghiệm điện ảnh chuẩn Netflix trước khi bước sang tái thiết giao diện khám phá ở `UI-P3`:

- **Loại bỏ 100% phim giả lập hư cấu** (`"Phim mo phong Beta Giai Phong"`, poster SVG bịa tên phim, điểm sao giả hoặc bộ đếm lượt xem ảo) khỏi dữ liệu hạt giống mặc định, bộ fixture E2E và API công khai.
- **Mở rộng có kiểm soát bảng `dbo.Movies`** thêm **29 cột `NULL`-able** lưu trữ định danh chuẩn quốc tế (`TmdbId`, `ImdbId`, `WikidataQid`), thông tin tác phẩm thật, ảnh đa kích thước cục bộ, xuất xứ bản quyền ảnh, điểm đánh giá có nguồn kiểm chứng kèm số lượt bình chọn, nhãn phân loại độ tuổi đúng chuẩn Việt Nam (`AgeLabelMarket = 'VN'`), vòng đời lưu đệm `TTL 180 ngày` và mặt nạ bảo vệ chỉnh sửa thủ công (`ManualOverrideMask`).
- **Không đụng chạm** cơ sở dữ liệu vận hành, khóa mật mã `AES-256-GCM` / `HMAC-SHA256`, hay bất kỳ nghiệp vụ giữ ghế – thanh toán – phát hành vé QR – soát vé nào.

---

## 2. Mở Rộng Lược Đồ `dbo.Movies` & Đồng Bộ Tuyệt Đối Bộ Tạo Tác Kiểm Soát Tĩnh

### 2.1. Các nhóm cột mới trên `dbo.Movies` (`116` → `145` cột trên toàn hệ thống 17 bảng)
- **Định danh đối chiếu đa nguồn & chỉ mục duy nhất có lọc (`WHERE ... IS NOT NULL`):**
  - `TmdbId` (`INT NULL`) — `UX_Movies_TmdbId`
  - `ImdbId` (`VARCHAR(16) NULL`) — `UX_Movies_ImdbId`
  - `WikidataQid` (`VARCHAR(16) NULL`) — `UX_Movies_WikidataQid`
- **Thông tin tác phẩm điện ảnh xác thực:** `OriginalTitle`, `ReleaseYear` (`CK_Movies_04: 1888..2100`), `ReleaseDateVn`, `OriginCountry`, `OriginalLanguage`, `DirectorNames`, `CastJson` (`CK_Movies_08: ISJSON(CastJson) = 1`), `OfficialTrailerYoutubeId`, `OfficialTrailerSource`.
- **Ảnh tác phẩm đa kích thước & ghi nhận bản quyền:** `PosterW342Url`, `BackdropUrl`, `BackdropW780Url`, `TitleLogoUrl`, `ArtworkSource`, `ArtworkLicenseText`, `ArtworkAttributionText`.
- **Điểm đánh giá có nguồn xác minh & quy mô bình chọn:** `RatingSource`, `RatingValue`, `RatingScaleMax`, `RatingVoteCount`, `RatingFetchedUtc` — bảo vệ bởi `CK_Movies_05` (`RatingValue > 0 AND RatingScaleMax > 0 AND RatingValue <= RatingScaleMax`) và `CK_Movies_06` (`RatingVoteCount >= 0`).
- **Quản trị nhãn độ tuổi thị trường Việt Nam & vòng đời đồng bộ:** `AgeLabelMarket` (`CK_Movies_03: 'VN'`), `AgeLabelSource`, `MetadataSource`, `ExternalSyncedUtc`, `ExternalExpiresUtc`, `ManualOverrideMask` (`CK_Movies_07 >= 0`).

### 2.2. EF Core 10 Migration & Đối soát lược đồ tĩnh
- Đã tạo migration chính thức [`20261103051835_RealMovieProvenanceSchema.cs`](../../src/BetaGiaiPhong.Data/Persistence/Migrations/20261103051835_RealMovieProvenanceSchema.cs) và đồng bộ 100% với:
  - [`sql/01_reference_schema_SQL_ONLY.sql`](../../sql/01_reference_schema_SQL_ONLY.sql) (`39` lô SQL chuẩn hóa)
  - [`docs/Data_Dictionary_145_Columns.csv`](../Data_Dictionary_145_Columns.csv) (`145` dòng định nghĩa cột)
  - [`docs/model_manifest.json`](../model_manifest.json) & [`scripts/static_schema_checks.py`](../../scripts/static_schema_checks.py)

---

## 3. Đường Ống Ảnh Đa Kích Thước Cục Bộ & Bộ Danh Mục 11 Tác Phẩm Thật

### 3.1. Kho ảnh xác minh cục bộ (`wwwroot/media/movies/verified/`)
- Đóng gói cục bộ **38 tệp ảnh JPEG thực tế** (`20` ảnh gốc poster `2:3` / backdrop `16:9` / still khung hình thật + `9` bản thu nhỏ poster `-w342.jpg` + `9` bản thu nhỏ backdrop `-w780.jpg`) kèm sổ cái kiểm toán [`ASSET_MANIFEST.json`](../../src/BetaGiaiPhong.Web/wwwroot/media/movies/verified/ASSET_MANIFEST.json) chứa kích thước pixel, dung lượng byte, mã băm `SHA-256`, URL nguồn gốc và giấy phép sử dụng (`Public Domain` / `CC BY 3.0` / `CC BY 4.0`).
- Thay thế hoàn toàn hình thế chỗ cũ bằng SVG trung tính [`poster-fallback.svg`](../../src/BetaGiaiPhong.Web/wwwroot/images/poster-fallback.svg) trên nền `#18181B` hiển thị thông báo trung thực **`CHƯA CÓ HÌNH ẢNH — Chờ đồng bộ ảnh tác phẩm chính thức`**, tuyệt đối không giả danh poster thương mại.

### 3.2. Gói 11 Tác phẩm Điện ảnh Thật (`VerifiedShowcaseCatalog`)
Được định nghĩa chuẩn xác tại [`MovieContentSyncService.cs`](../../src/BetaGiaiPhong.Data/Services/MovieContentSyncService.cs) và nạp tự động qua `DemoSeeder` / `Runner seed-verified-showcase`:
1. **4 kiệt tác kinh điển Phạm vi Công cộng (Public Domain — Wikimedia Commons + Wikidata + IMDb Non-Commercial):**
   - *Mê trong mê* (*Charade*, 1963 — `Q496266`, `tt0056923`, đạo diễn Stanley Donen, Cary Grant & Audrey Hepburn, `IMDb 7.8/10` với `95.223` lượt, trailer chính thức `BJ_njww9SOQ`).
   - *Night of the Living Dead* (1968 — `Q622435`, `tt0063350`, đạo diễn George A. Romero, `IMDb 7.8/10`).
   - *His Girl Friday* (1940 — `Q599097`, `tt0032599`, đạo diễn Howard Hawks, `IMDb 7.8/10`).
   - *The General* (1926 — `Q237116`, `tt0017925`, đạo diễn Buster Keaton & Clyde Bruckman, `IMDb 8.1/10`).
2. **5 phim mở chuẩn điện ảnh Blender Studio (`CC BY 3.0` / `CC BY 4.0` — Poster & Khung hình 16:9 chính chủ):**
   - *Sintel* (2010 — `Q746050`), *Tears of Steel* (2012 — `Q1572496`), *Cosmos Laundromat* (2015 — `Q20856863`), *Spring* (2019 — `Q62925885`), *Charge* (2022 — `Q115804642`).
3. **2 tác phẩm điện ảnh Việt Nam đương đại xác thực (`Wikidata` + Nhãn phân loại Cục Điện ảnh VN):**
   - *Mai* (2024 — `Q124516759`, `tt29932155`, đạo diễn Trấn Thành, nhãn **`T18`** thị trường `VN`, trạng thái ảnh `PENDING_TMDB_API_KEY` trung thực khi chưa cấu hình khóa TMDB).
   - *Đào, phở và piano* (2024 — `Q124632613`, `tt31451096`, đạo diễn Phi Tiến Sơn, nhãn **`T13`** thị trường `VN`, trạng thái ảnh `PENDING_TMDB_API_KEY`).

---

## 4. Dịch Vụ Đồng Bộ `MovieContentSyncService` & Hợp Đồng API Có Nguồn Gốc

### 4.1. Các cơ chế bảo vệ dữ liệu thật trong `MovieContentSyncService`
- **Đồng bộ TMDB API v3 theo yêu cầu (`Runner sync-tmdb-movie <tmdbId>`):**
  - Đọc khóa từ biến môi trường `BGP_TMDB_READ_TOKEN` (hoặc `TMDB_API_READ_ACCESS_TOKEN`). Khi thiếu khóa, trả về mã `MISSING_API_KEY` an toàn và tuyệt đối không ném ngoại lệ làm sập ứng dụng.
  - Phim mới lấy từ TMDB luôn khởi tạo ở trạng thái **`DRAFT`** để Quản lý rạp kiểm duyệt trước khi xuất bản.
  - **Không bao giờ tự động quy đổi** nhãn phân loại Mỹ (`PG-13`, `R`) sang nhãn Việt Nam (`P`, `K`, `T13`, `T16`, `T18`); chỉ nhận chứng nhận thuộc vùng `iso_3166_1 == "VN"` hợp lệ.
  - Áp dụng `ExternalExpiresUtc = UtcNow + 180 ngày`. Khi TMDB lỗi mạng hoặc trả mã lỗi HTTP (`EXTERNAL_ERROR`), giữ nguyên 100% bản lưu cục bộ trước đó và **không bao giờ in chuỗi Bearer token** ra thông báo lỗi hoặc nhật ký.
- **Bảo vệ chỉnh sửa của Quản lý rạp (`ManualOverrideMask`):**
  - [`MovieManagementService.cs`](../../src/BetaGiaiPhong.Data/Services/MovieManagementService.cs) tự động bật cờ bit trong `ManualOverrideMask` trên từng nhóm trường (`Title`, `Duration`, `AgeLabel`, `Genre`, `Synopsis`, `Poster`) khi Quản lý rạp chỉnh sửa tay, ngăn các lần đồng bộ lại ghi đè quyết định biên tập nội bộ.
- **Nhập luồng tập dữ liệu phi thương mại IMDb (`Runner sync-imdb-ratings <path>`):**
  - Đọc luồng trực tiếp tệp `title.ratings.tsv` / `.tsv.gz` đối chiếu theo `ImdbId` đã có trong CSDL, cập nhật `RatingSource = 'IMDB'`, `RatingValue`, `RatingScaleMax = 10.0` và `RatingVoteCount`.

### 4.2. Mở rộng DTO, Tuyển chọn Hero & Endpoint `quick-preview`
- Mở rộng [`MovieDtos.cs`](../../src/BetaGiaiPhong.Web/Models/Responses/MovieDtos.cs) và [`CatalogService.cs`](../../src/BetaGiaiPhong.Web/Services/CatalogService.cs) bổ sung `MovieRatingDto` (luôn kèm nguồn và số lượt đánh giá, trả `null` nếu thiếu nguồn), `MovieArtworkDto` (đa kích thước `PosterW342Url`, `BackdropW780Url`, cờ `HasVerifiedPoster` / `HasVerifiedBackdrop`), `MovieCastMemberDto`, `MovieProvenanceDto` và `IsHeroEligible`.
- **Quy tắc tuyển chọn Hero (`HomeController`):** Chỉ các phim thỏa `IsHeroEligible == true` (có cả Poster và Backdrop xác thực) mới được đưa lên vị trí Hero Billboard đầu trang; phim thiếu backdrop (`PENDING_TMDB_API_KEY` hoặc chỉ có poster dọc) vẫn xuất hiện trung thực trong danh mục nhưng không bao giờ làm vỡ bố cục Hero ngang.
- **Sắp xếp lịch chiếu thông minh & Tìm kiếm đa trường:** Danh mục phim ưu tiên các tác phẩm có suất chiếu sắp diễn ra sớm nhất (`NextStartUtc`), đồng thời `/movies?q=...` tìm khớp trên cả `Title`, `OriginalTitle`, `GenreText` và `DirectorNames`.
- **Endpoint đọc nhẹ mới `GET /api/movies/{id}/quick-preview` ([`MoviesApiController.cs`](../../src/BetaGiaiPhong.Web/Api/MoviesApiController.cs)):** Cung cấp dữ liệu gọn nhẹ cho thẻ xem nhanh (Hover Quick-Preview Card ở `UI-P3`/`P4`), bao gồm tóm tắt ngắn, điểm đánh giá xác thực, mã YouTube trailer chính thức và tối đa 3 khung giờ chiếu `ON_SALE` kế tiếp theo giờ Việt Nam (`UTC+7`).
- **Chuẩn hóa mã hóa UTF-8 Tiếng Việt ([`Program.cs`](../../src/BetaGiaiPhong.Web/Program.cs)):** Cấu hình `WebEncoderOptions` với `UnicodeRanges.All` giúp HTML kết xuất từ máy chủ hiển thị trực tiếp ký tự tiếng Việt chuẩn xác thay vì chuỗi thực thể thập lục phân.

---

## 5. Danh Mục Tệp Đã Tạo Mới & Cập Nhật Trong `BE/DATA-P2`

| Nhóm | Đường dẫn tệp | Thao tác | Mô tả chi tiết |
|---|---|:---:|---|
| **Thực thể & EF Core** | [`src/BetaGiaiPhong.Data/Entities/Movie.cs`](../../src/BetaGiaiPhong.Data/Entities/Movie.cs) | Cập nhật | Bổ sung 29 cột nguồn gốc/ảnh/điểm/diễn viên/TTL, `MovieOverrideFlags` và `IsHeroEligible`. |
| **Thực thể & EF Core** | [`src/BetaGiaiPhong.Data/Persistence/BetaModelConfiguration.cs`](../../src/BetaGiaiPhong.Data/Persistence/BetaModelConfiguration.cs) | Cập nhật | Ánh xạ kiểu cột SQL, `CK_Movies_03`..`CK_Movies_08` và 3 chỉ mục duy nhất có lọc. |
| **Migration** | [`src/BetaGiaiPhong.Data/Persistence/Migrations/20261103051835_RealMovieProvenanceSchema.cs`](../../src/BetaGiaiPhong.Data/Persistence/Migrations/20261103051835_RealMovieProvenanceSchema.cs) + [`BetaDbContextModelSnapshot.cs`](../../src/BetaGiaiPhong.Data/Persistence/Migrations/BetaDbContextModelSnapshot.cs) | Tạo mới / Cập nhật | Migration mở rộng `dbo.Movies` hai chiều `Up` / `Down` an toàn, không còn thay đổi mô hình chờ xử lý. |
| **Tạo tác Lược đồ** | [`sql/01_reference_schema_SQL_ONLY.sql`](../../sql/01_reference_schema_SQL_ONLY.sql), [`docs/Data_Dictionary_145_Columns.csv`](../Data_Dictionary_145_Columns.csv), [`docs/model_manifest.json`](../model_manifest.json), [`scripts/static_schema_checks.py`](../../scripts/static_schema_checks.py) | Cập nhật / Tạo mới | Đồng bộ 17 bảng, 145 cột, 22 khóa ngoại và 39 lô SQL với bộ kiểm tra tĩnh. |
| **Kho Ảnh Cục bộ** | `src/BetaGiaiPhong.Web/wwwroot/media/movies/verified/*` (38 ảnh + [`ASSET_MANIFEST.json`](../../src/BetaGiaiPhong.Web/wwwroot/media/movies/verified/ASSET_MANIFEST.json)), [`poster-fallback.svg`](../../src/BetaGiaiPhong.Web/wwwroot/images/poster-fallback.svg) | Tạo mới / Cập nhật | Bộ ảnh poster/backdrop đa kích thước đã xác minh bản quyền và hình thế chỗ trung tính `#18181B`. |
| **Dịch vụ Đồng bộ** | [`src/BetaGiaiPhong.Data/Services/MovieContentSyncService.cs`](../../src/BetaGiaiPhong.Data/Services/MovieContentSyncService.cs), [`MovieManagementService.cs`](../../src/BetaGiaiPhong.Data/Services/MovieManagementService.cs), [`ServiceRegistration.cs`](../../src/BetaGiaiPhong.Data/ServiceRegistration.cs) | Tạo mới / Cập nhật | Hạt giống 11 tác phẩm thật, đồng bộ TMDB v3 an toàn, đọc luồng TSV IMDb và bảo vệ `ManualOverrideMask`. |
| **CLI Runner** | [`src/BetaGiaiPhong.Runner/DemoSeeder.cs`](../../src/BetaGiaiPhong.Runner/DemoSeeder.cs), [`src/BetaGiaiPhong.Runner/Program.cs`](../../src/BetaGiaiPhong.Runner/Program.cs) | Cập nhật | Bổ sung `seed-verified-showcase`, `sync-tmdb-movie`, `sync-imdb-ratings`; chuyển toàn bộ fixture E2E sang phim thật. |
| **Web API & MVC** | [`MovieDtos.cs`](../../src/BetaGiaiPhong.Web/Models/Responses/MovieDtos.cs), [`CatalogService.cs`](../../src/BetaGiaiPhong.Web/Services/CatalogService.cs), [`MoviesApiController.cs`](../../src/BetaGiaiPhong.Web/Api/MoviesApiController.cs), [`HomeController.cs`](../../src/BetaGiaiPhong.Web/Controllers/HomeController.cs), [`MoviesController.cs`](../../src/BetaGiaiPhong.Web/Controllers/MoviesController.cs), [`Program.cs`](../../src/BetaGiaiPhong.Web/Program.cs) | Cập nhật | Hợp đồng DTO nguồn gốc phim thật, endpoint `/api/movies/{id}/quick-preview`, lọc Hero và tìm kiếm đa trường. |
| **Kiểm thử xUnit** | [`tests/BetaGiaiPhong.Tests/IntegrationTests/MovieContentSyncTests.cs`](../../tests/BetaGiaiPhong.Tests/IntegrationTests/MovieContentSyncTests.cs), [`PublicCatalogWebTests.cs`](../../tests/BetaGiaiPhong.Tests/WebTests/PublicCatalogWebTests.cs) | Tạo mới / Cập nhật | Kiểm thử tích hợp toàn diện quy trình nạp 11 phim thật, TMDB mock HTTP, bảo vệ secret, `ManualOverrideMask`, TSV IMDb và API `quick-preview`. |
| **Playwright E2E** | [`e2e/tests/10-verified-movie-content.spec.ts`](../../e2e/tests/10-verified-movie-content.spec.ts), [`02-negative-and-security.spec.ts`](../../e2e/tests/02-negative-and-security.spec.ts), [`03-restart-recovery.spec.ts`](../../e2e/tests/03-restart-recovery.spec.ts), [`e2e/tools/capture-ui.ts`](../../e2e/tools/capture-ui.ts) | Tạo mới / Cập nhật | Kịch bản E2E xác minh 11 phim thật, tải thành công ảnh đa kích thước cục bộ, Hero xác thực và công cụ chụp bằng chứng `be-data-p2`. |

---

## 6. Kết Quả Kiểm Chứng Kỹ Thuật Toàn Diện

| Hạng mục kiểm chứng | Lệnh thực thi | Kết quả |
|---|---|:---:|
| **1. Kiểm tra tĩnh lược đồ & Ràng buộc C# / SQL** | `python scripts/static_schema_checks.py` | **PASSED** (`17 tables, 145 columns, 22 FKs; 39 SQL batches`) |
| **2. Kiểm tra đồng bộ EF Core Model & Migration** | `dotnet ef migrations has-pending-model-changes --project src/BetaGiaiPhong.Data` | **PASSED** (`0 pending model changes`) |
| **3. Biên dịch toàn bộ Solution (`Release`)** | `dotnet build -c Release --nologo` | **PASSED** (`0 Warning(s), 0 Error(s)`) |
| **4. Kiểm thử Đơn vị & Tích hợp xUnit** | `dotnet test -c Release --no-build --nologo` (trên DB tạm tự hủy) | **PASSED** (`171/171 passed, 0 failed`) |
| **5. Kiểm tra kiểu TypeScript E2E** | `npx tsc --noEmit` (trong `e2e/`) | **PASSED** (`0 errors`) |
| **6. Kiểm thử Trình duyệt Thực Playwright (`01` → `10`)** | `npx playwright test` | **PASSED** (`28/28 passed, 0 failed`) |
| **7. Đối soát Toàn vẹn Nghiệp vụ (`Runner verify`)** | Thực thi tự động cuối chu kỳ E2E & Capture | **INTEGRITY PASSED** (`0 violation(s)`) |

---

## 7. Bộ Ảnh Bằng Chứng Nghiệm Thu (`docs/ui/screenshots/be-data-p2/`)

Toàn bộ 5 ảnh dưới đây được chụp tự động từ máy chủ Kestrel chạy thực tế trên cơ sở dữ liệu tạm và đã được kiểm tra trực quan từng khung hình:

1. [`01-api-movies-verified-json.png`](screenshots/be-data-p2/01-api-movies-verified-json.png) — Kết quả thực tế `GET /api/movies`: `11/11` tác phẩm thật, `0` phim hư cấu, kèm đối tượng `rating` (`IMDb 7.8/10 (95.223 lượt)`), `artwork` đa kích thước cục bộ và `provenance` (`Q496266`, `tt0056923`).
2. [`02-api-quick-preview-charade-json.png`](screenshots/be-data-p2/02-api-quick-preview-charade-json.png) — Kết quả thực tế `GET /api/movies/1/quick-preview`: trả về tóm tắt ngắn, mã trailer chính thức `BJ_njww9SOQ`, điểm IMDb xác thực và 3 khung giờ chiếu kế tiếp theo múi giờ Việt Nam (`UTC+7`).
3. [`03-home-hero-verified-charade-desktop.png`](screenshots/be-data-p2/03-home-hero-verified-charade-desktop.png) — Trang chủ (`1440×900`) tuyển chọn phim có cả Poster & Backdrop xác thực lên Hero Billboard (*His Girl Friday*, 1940) cùng hàng phim đang mở bán gồm toàn bộ các tác phẩm điện ảnh thật (*His Girl Friday*, *Charge*, *Tears of Steel*, *Mê trong mê / Charade*, *Night of the Living Dead*, *Sintel*).
4. [`04-catalog-verified-11-films-desktop.png`](screenshots/be-data-p2/04-catalog-verified-11-films-desktop.png) — Toàn cảnh trang Danh mục phim (`/movies`, `1440×900`) hiển thị đầy đủ 11 tác phẩm thật đã xuất bản, không còn bất kỳ mục `"Phim mo phong"` nào.
5. [`05-catalog-pending-tmdb-placeholder-desktop.png`](screenshots/be-data-p2/05-catalog-pending-tmdb-placeholder-desktop.png) — Chế độ lọc `Chưa có lịch chiếu` (`/movies?filter=no-showtimes`) đối chiếu trực quan giữa ảnh mở đã xác minh (*Cosmos Laundromat*, *The General*) và thẻ chờ trung tính `#18181B` **`CHƯA CÓ HÌNH ẢNH`** kèm nhãn độ tuổi chuẩn Việt Nam (**`T13`**, **`T18`**) của *Đào, phở và piano* và *Mai*.

---

## 8. Sẵn Sàng Chuyển Giao Sang Giai Đoạn 3 (`UI-P3`)

Toàn bộ tầng dữ liệu phim thật, kho ảnh đa kích thước cục bộ, bộ quy tắc bảo vệ nguồn gốc và hợp đồng API `quick-preview` của `BE/DATA-P2` đã hoàn tất 100% và vượt qua trọn vẹn mọi bài kiểm thử tĩnh, tích hợp và trình duyệt thực. Hệ thống đã sẵn sàng bước sang **Giai đoạn 3 (`UI-P3`: Tái thiết Trang chủ Điện ảnh chuẩn Netflix — Hero Billboard Toàn Chiều Ngang, Hàng Phim Ngang Có Nút Trượt, Hover Quick-Preview Card & Modal Trailer Chính thức)** với `UseLegacyBootstrap = false`.
