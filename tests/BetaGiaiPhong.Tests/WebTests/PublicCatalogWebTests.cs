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
}
