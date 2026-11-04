using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Tests.IntegrationTests;
using BetaGiaiPhong.Web.Models.Responses;
using Xunit;

namespace BetaGiaiPhong.Tests.WebTests;

[Collection("DatabaseCollection")]
public sealed class PublicCatalogWebTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory factory;
    private readonly TestDatabaseFixture fixture;
    private readonly HttpClient client;

    public PublicCatalogWebTests(TestDatabaseFixture fixture)
    {
        this.fixture = fixture;
        factory = new CustomWebApplicationFactory(fixture);
        client = factory.CreateClient();
    }

    [Fact]
    public async Task TC_WEB_CAT_01_GetMovies_ReturnsPublishedMoviesOnly()
    {
        // Act
        var response = await client.GetAsync("/api/movies");

        // Assert
        response.EnsureSuccessStatusCode();
        var movies = await response.Content.ReadFromJsonAsync<List<MovieSummaryDto>>();
        Assert.NotNull(movies);
        Assert.NotEmpty(movies);
        Assert.All(movies, m => Assert.Equal("PUBLISHED", m.Status));
        Assert.DoesNotContain(movies, m => m.Title.Contains("Phim mo phong", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TC_WEB_CAT_02_UnpublishedMovie_ReturnsNotFoundOnDetailsAndApi()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var draftMovie = await db.Movies.FirstOrDefaultAsync(m => m.Status == "DRAFT");
        if (draftMovie is null)
        {
            draftMovie = new Movie
            {
                Title = "Phim Nhap Chua Xuat Ban",
                DurationMin = 90,
                GenreText = "Draft",
                Status = "DRAFT"
            };
            db.Movies.Add(draftMovie);
            await db.SaveChangesAsync();
        }

        // 1. Api call
        var apiResp = await client.GetAsync($"/api/movies/{draftMovie.MovieId}");
        Assert.Equal(HttpStatusCode.NotFound, apiResp.StatusCode);

        // 2. Quick-preview API call
        var previewResp = await client.GetAsync($"/api/movies/{draftMovie.MovieId}/quick-preview");
        Assert.Equal(HttpStatusCode.NotFound, previewResp.StatusCode);

        // 3. MVC call
        var mvcResp = await client.GetAsync($"/movies/{draftMovie.MovieId}");
        Assert.Equal(HttpStatusCode.NotFound, mvcResp.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_CAT_03_NonExistentMovie_Returns404ProblemDetails()
    {
        var response = await client.GetAsync("/api/movies/99999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task TC_WEB_CAT_04_GetShowtimes_ReturnsCorrectUtcAndVietnamFormattedDates()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var showtime = await db.Showtimes
            .Include(s => s.Movie)
            .FirstAsync(s => s.Status == "ON_SALE" && s.Movie != null && s.Movie.Status == "PUBLISHED");

        var response = await client.GetAsync($"/api/movies/{showtime.MovieId}/showtimes");
        response.EnsureSuccessStatusCode();

        var showtimes = await response.Content.ReadFromJsonAsync<List<ShowtimeDto>>();
        Assert.NotNull(showtimes);
        Assert.Contains(showtimes, s => s.ShowtimeId == showtime.ShowtimeId);

        var target = showtimes.First(s => s.ShowtimeId == showtime.ShowtimeId);
        Assert.False(string.IsNullOrWhiteSpace(target.StartVietnamText));
        Assert.Contains(":", target.StartVietnamText);
        Assert.Equal(showtime.Movie!.Title, target.MovieTitle);
    }

    [Fact]
    public async Task TC_WEB_CAT_05_GetSeatMap_ReturnsCorrectSeatHierarchyAndPrices()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var showtime = await db.Showtimes
            .Include(s => s.Movie)
            .Include(s => s.SeatPrices)
            .FirstAsync(s => s.Status == "ON_SALE" && s.Movie != null && s.Movie.Status == "PUBLISHED");

        var response = await client.GetAsync($"/api/showtimes/{showtime.ShowtimeId}/seats");
        response.EnsureSuccessStatusCode();

        var seatMap = await response.Content.ReadFromJsonAsync<SeatMapDto>();
        Assert.NotNull(seatMap);
        Assert.Equal(showtime.ShowtimeId, seatMap.ShowtimeId);
        Assert.NotEmpty(seatMap.Seats);
        Assert.True(seatMap.PriceTiersVnd.Count > 0);

        // Verify seats have positive prices and valid rows
        foreach (var seat in seatMap.Seats)
        {
            Assert.False(string.IsNullOrWhiteSpace(seat.SeatCode));
            Assert.False(string.IsNullOrWhiteSpace(seat.RowLabel));
            Assert.True(seat.PriceVnd > 0);
        }
    }

    [Fact]
    public async Task TC_WEB_CAT_06_GetSeatMap_ReadOnly_DoesNotMutateDatabase()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var showtime = await db.Showtimes.FirstAsync(s => s.Status == "ON_SALE");

        // Record states and rowversions before GET request
        var seatsBefore = await db.ShowtimeSeats
            .Where(ss => ss.ShowtimeId == showtime.ShowtimeId)
            .Select(ss => new { ss.SeatId, ss.State, ss.RowVer, ss.OwnerOrderId })
            .ToListAsync();

        // Perform GET seat map via MVC and API
        var apiResp = await client.GetAsync($"/api/showtimes/{showtime.ShowtimeId}/seats");
        apiResp.EnsureSuccessStatusCode();

        var mvcResp = await client.GetAsync($"/showtimes/{showtime.ShowtimeId}/seats");
        mvcResp.EnsureSuccessStatusCode();

        // Inspect states after GET request
        await using var dbAfter = await fixture.CreateDbContextAsync();
        var seatsAfter = await dbAfter.ShowtimeSeats
            .Where(ss => ss.ShowtimeId == showtime.ShowtimeId)
            .Select(ss => new { ss.SeatId, ss.State, ss.RowVer, ss.OwnerOrderId })
            .ToListAsync();

        Assert.Equal(seatsBefore.Count, seatsAfter.Count);
        for (int i = 0; i < seatsBefore.Count; i++)
        {
            Assert.Equal(seatsBefore[i].SeatId, seatsAfter[i].SeatId);
            Assert.Equal(seatsBefore[i].State, seatsAfter[i].State);
            Assert.Equal(seatsBefore[i].OwnerOrderId, seatsAfter[i].OwnerOrderId);
            Assert.Equal(seatsBefore[i].RowVer, seatsAfter[i].RowVer);
        }
    }

    [Fact]
    public async Task TC_WEB_CAT_07_MovieApisAndQuickPreview_ReturnGroundedProvenanceRatingCastAndHeroEligibility()
    {
        var listResp = await client.GetAsync("/api/movies");
        listResp.EnsureSuccessStatusCode();
        var movies = await listResp.Content.ReadFromJsonAsync<List<MovieSummaryDto>>();
        Assert.NotNull(movies);

        var charade = Assert.Single(movies, m => m.Provenance?.WikidataQid == "Q496266");
        Assert.Equal("Mê trong mê", charade.Title);
        Assert.Equal("Charade", charade.OriginalTitle);
        Assert.Equal((short)1963, charade.ReleaseYear);
        Assert.Equal("Stanley Donen", charade.DirectorNames);
        Assert.Equal("BJ_njww9SOQ", charade.OfficialTrailerYoutubeId);
        Assert.True(charade.IsHeroEligible);
        Assert.NotNull(charade.Rating);
        Assert.Equal("IMDB", charade.Rating!.Source);
        Assert.Equal(7.8m, charade.Rating.Score);
        Assert.Equal(10.0m, charade.Rating.ScaleMax);
        Assert.Equal(95223, charade.Rating.VoteCount);
        Assert.Contains("IMDb 7.8/10", charade.Rating.DisplayText);
        Assert.NotNull(charade.Artwork);
        Assert.True(charade.Artwork!.HasVerifiedPoster);
        Assert.True(charade.Artwork.HasVerifiedBackdrop);
        Assert.Equal("/media/movies/verified/charade-1963-poster-w342.jpg", charade.Artwork.PosterW342Url);
        Assert.Equal("/media/movies/verified/charade-1963-backdrop-w780.jpg", charade.Artwork.BackdropW780Url);

        var mai = Assert.Single(movies, m => m.Provenance?.WikidataQid == "Q124516759");
        Assert.False(mai.IsHeroEligible);
        Assert.Equal("T18", mai.AgeLabel);
        Assert.Equal("VN", mai.Provenance?.AgeLabelMarket);
        Assert.Equal("PENDING_TMDB_API_KEY", mai.Artwork?.Source);
        Assert.False(mai.Artwork?.HasVerifiedPoster ?? true);
        Assert.False(mai.Artwork?.HasVerifiedBackdrop ?? true);

        // Detail endpoint includes verified Cast array
        var detailResp = await client.GetAsync($"/api/movies/{charade.MovieId}");
        detailResp.EnsureSuccessStatusCode();
        var detail = await detailResp.Content.ReadFromJsonAsync<MovieDetailDto>();
        Assert.NotNull(detail);
        Assert.NotNull(detail!.Cast);
        Assert.Contains(detail.Cast!, c => c.Name == "Cary Grant" && c.Character == "Peter Joshua");
        Assert.Contains(detail.Cast!, c => c.Name == "Audrey Hepburn");

        // Quick-preview endpoint returns compact grounded DTO
        var previewResp = await client.GetAsync($"/api/movies/{charade.MovieId}/quick-preview");
        previewResp.EnsureSuccessStatusCode();
        var preview = await previewResp.Content.ReadFromJsonAsync<MovieQuickPreviewDto>();
        Assert.NotNull(preview);
        Assert.Equal("Mê trong mê", preview!.Title);
        Assert.Equal("Charade", preview.OriginalTitle);
        Assert.Equal("BJ_njww9SOQ", preview.OfficialTrailerYoutubeId);
        Assert.NotNull(preview.Rating);
        Assert.True(preview.Artwork.HasVerifiedBackdrop);

        // Homepage renders Hero with Hero-eligible movie (Mê trong mê) and never selects backdrop-less Mai as Hero
        var homeResp = await client.GetAsync("/");
        homeResp.EnsureSuccessStatusCode();
        var homeHtml = await homeResp.Content.ReadAsStringAsync();
        Assert.Contains("Mê trong mê", homeHtml);
        Assert.DoesNotContain("Phim mo phong", homeHtml);

        // Catalogue search by OriginalTitle ("Charade") and DirectorNames ("Trấn Thành") works cleanly
        var searchOriginalResp = await client.GetAsync("/movies?q=Charade");
        searchOriginalResp.EnsureSuccessStatusCode();
        var searchOriginalHtml = await searchOriginalResp.Content.ReadAsStringAsync();
        Assert.Contains("Mê trong mê", searchOriginalHtml);

        var searchDirectorResp = await client.GetAsync("/movies?q=Tr%E1%BA%A5n%20Th%C3%A0nh");
        searchDirectorResp.EnsureSuccessStatusCode();
        var searchDirectorHtml = await searchDirectorResp.Content.ReadAsStringAsync();
        Assert.Contains("Mai", searchDirectorHtml);
    }

    [Fact]
    public async Task TC_WEB_CAT_08_HomepageBillboardCarousel_RendersUpToSixEligibleSlides_TopicBento_AndExcludesBackdropLessMovies()
    {
        var listResp = await client.GetAsync("/api/movies");
        listResp.EnsureSuccessStatusCode();
        var movies = await listResp.Content.ReadFromJsonAsync<List<MovieSummaryDto>>();
        Assert.NotNull(movies);

        var mai = Assert.Single(movies!, m => m.Provenance?.WikidataQid == "Q124516759");
        Assert.False(mai.IsHeroEligible);

        var homeResp = await client.GetAsync("/");
        homeResp.EnsureSuccessStatusCode();
        var homeHtml = await homeResp.Content.ReadAsStringAsync();

        var slideMatches = System.Text.RegularExpressions.Regex.Matches(
            homeHtml,
            @"data-testid=""billboard-slide""[\s\S]*?data-movie-id=""(\d+)""");

        Assert.InRange(slideMatches.Count, 2, 6);

        var slideMovieIds = slideMatches
            .Select(m => long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(slideMovieIds.Count, slideMovieIds.Distinct().Count());
        Assert.DoesNotContain(mai.MovieId, slideMovieIds);

        var indicatorMatches = System.Text.RegularExpressions.Regex.Matches(
            homeHtml,
            @"data-testid=""billboard-indicator""");
        Assert.Equal(slideMatches.Count, indicatorMatches.Count);

        var topicCardMatches = System.Text.RegularExpressions.Regex.Matches(
            homeHtml,
            @"data-testid=""topic-bento-card""");
        Assert.Equal(6, topicCardMatches.Count);

        Assert.Contains(@"data-testid=""acclaimed-showcase-section""", homeHtml);
        Assert.Contains(@"data-testid=""billboard-prev-btn""", homeHtml);
        Assert.Contains(@"data-testid=""billboard-next-btn""", homeHtml);
        Assert.Contains(@"data-testid=""billboard-video-toggle""", homeHtml);
        Assert.Contains(@"js/modules/billboard-carousel.js", homeHtml);
    }

    [Fact]
    public async Task TC_WEB_CAT_09_MovieCatalog_SupportsGenreAgeYearSortAndPaginationFilters()
    {
        var baseResp = await client.GetAsync("/movies");
        baseResp.EnsureSuccessStatusCode();
        var baseHtml = await baseResp.Content.ReadAsStringAsync();

        Assert.Contains(@"data-testid=""catalog-select-genre""", baseHtml);
        Assert.Contains(@"data-testid=""catalog-select-age""", baseHtml);
        Assert.Contains(@"data-testid=""catalog-select-year""", baseHtml);
        Assert.Contains(@"data-testid=""catalog-select-filter""", baseHtml);
        Assert.Contains(@"data-testid=""catalog-select-sort""", baseHtml);
        Assert.Contains(@"data-testid=""catalog-pagination""", baseHtml);

        // 1. Filter by Genre ("Kinh dị") -> includes Night of the Living Dead, excludes Mai
        var genreResp = await client.GetAsync("/movies?genre=Kinh%20d%E1%BB%8B");
        genreResp.EnsureSuccessStatusCode();
        var genreHtml = await genreResp.Content.ReadAsStringAsync();
        Assert.Contains("Night of the Living Dead", genreHtml);
        Assert.DoesNotContain(">Mai<", genreHtml);

        // 2. Filter by VN Age Classification ("T18" & "K")
        var ageResp = await client.GetAsync("/movies?age=T18");
        ageResp.EnsureSuccessStatusCode();
        var ageHtml = await ageResp.Content.ReadAsStringAsync();
        Assert.Contains("Mai", ageHtml);
        Assert.DoesNotContain("Mê trong mê", ageHtml);

        var ageKResp = await client.GetAsync("/movies?age=K");
        ageKResp.EnsureSuccessStatusCode();
        var ageKHtml = await ageKResp.Content.ReadAsStringAsync();
        Assert.Contains("Lật Mặt 7: Một Điều Ước", ageKHtml);

        // 3. Filter by Release Year (1963 & 2025)
        var yearResp = await client.GetAsync("/movies?year=1963");
        yearResp.EnsureSuccessStatusCode();
        var yearHtml = await yearResp.Content.ReadAsStringAsync();
        Assert.Contains("Mê trong mê", yearHtml);
        Assert.DoesNotContain(">Mai<", yearHtml);

        var year2025Resp = await client.GetAsync("/movies?year=2025");
        year2025Resp.EnsureSuccessStatusCode();
        var year2025Html = await year2025Resp.Content.ReadAsStringAsync();
        Assert.Contains("Địa Đạo: Mặt Trời Trong Bóng Tối", year2025Html);

        // Default /movies (31+ real works, pageSize=24) naturally spans 2 pages
        Assert.Contains("Trang <strong>1/2</strong>", baseHtml);

        // 4. Sort by IMDb Rating descending -> extracted card scores are non-increasing
        var sortResp = await client.GetAsync("/movies?sort=rating");
        sortResp.EnsureSuccessStatusCode();
        var sortHtml = await sortResp.Content.ReadAsStringAsync();
        var scoreMatches = System.Text.RegularExpressions.Regex.Matches(
            sortHtml,
            @"bgp-catalog-star[^>]*>★</span>\s*([0-9]+\.[0-9]+)");
        Assert.True(scoreMatches.Count >= 4);
        var scores = scoreMatches
            .Select(m => decimal.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        for (int i = 1; i < scores.Count; i++)
        {
            Assert.True(scores[i - 1] >= scores[i], $"Expected non-increasing IMDb score order at index {i}: {scores[i - 1]} < {scores[i]}");
        }

        // 5. Pagination (pageSize=6 -> Page 1 and Page 2 return distinct movie cards)
        var page1Resp = await client.GetAsync("/movies?pageSize=6&page=1");
        page1Resp.EnsureSuccessStatusCode();
        var page1Html = await page1Resp.Content.ReadAsStringAsync();

        var page2Resp = await client.GetAsync("/movies?pageSize=6&page=2");
        page2Resp.EnsureSuccessStatusCode();
        var page2Html = await page2Resp.Content.ReadAsStringAsync();

        var page1Ids = System.Text.RegularExpressions.Regex.Matches(page1Html, @"data-preview-movie-id=""(\d+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();
        var page2Ids = System.Text.RegularExpressions.Regex.Matches(page2Html, @"data-preview-movie-id=""(\d+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(6, page1Ids.Count);
        Assert.NotEmpty(page2Ids);
        Assert.Empty(page1Ids.Intersect(page2Ids));
        Assert.Contains("Trang <strong>2/", page2Html);
    }

    [Fact]
    public async Task TC_WEB_CAT_10_MovieDetail_RendersRapPhimStageHeroSubTabsDossierAndRankedRecommendations()
    {
        var listResp = await client.GetAsync("/api/movies");
        listResp.EnsureSuccessStatusCode();
        var movies = await listResp.Content.ReadFromJsonAsync<List<MovieSummaryDto>>();
        Assert.NotNull(movies);

        var charade = Assert.Single(movies!, m => m.Provenance?.WikidataQid == "Q496266");

        var detailResp = await client.GetAsync($"/movies/{charade.MovieId}");
        detailResp.EnsureSuccessStatusCode();
        var html = await detailResp.Content.ReadAsStringAsync();

        // 1. RapPhim Stage Hero + Title Treatment + Action Bar
        Assert.Contains("bgp-title-treatment", html);
        Assert.Contains(@"data-testid=""detail-cta-scroll-showtimes""", html);
        Assert.Contains(@"data-testid=""detail-share-btn""", html);
        Assert.Contains(@"js/modules/detail-tabs.js", html);

        // 2. White-underline 4-tab bar (Trailer tab replaces Dossier; consumer Provenance Card is completely removed)
        Assert.Contains(@"data-testid=""detail-subtabs""", html);
        Assert.Contains(@"data-testid=""detail-tab-info""", html);
        Assert.Contains(@"data-testid=""detail-tab-cast""", html);
        Assert.Contains(@"data-testid=""detail-tab-trailer""", html);
        Assert.Contains(@"data-testid=""detail-tab-recommended""", html);
        Assert.DoesNotContain(@"data-testid=""detail-provenance-card""", html);
        Assert.DoesNotContain("Hồ sơ Nguồn Dữ liệu Tác phẩm", html);

        // 3. 3-column asymmetric workspace: Left Dossier Card + Center Stack + Right Ranked 1->8 Sidebar
        Assert.Contains(@"data-testid=""detail-workspace""", html);
        Assert.Contains(@"data-testid=""detail-poster-dossier""", html);
        Assert.Contains(@"data-testid=""detail-ranked-sidebar""", html);
        var rankedMatches = System.Text.RegularExpressions.Regex.Matches(html, @"data-testid=""detail-ranked-item""");
        Assert.InRange(rankedMatches.Count, 4, 8);

        // 4. Strict domain & integrity guardrails (no fake streaming UI or vote counts)
        Assert.DoesNotContain("Chọn Server", html);
        Assert.DoesNotContain("Nhóm Telegram", html);
        Assert.DoesNotContain("Top 10 tại Việt Nam", html);
        Assert.DoesNotContain("98% phù hợp", html);
        Assert.DoesNotContain("lượt)", html);
    }
}
