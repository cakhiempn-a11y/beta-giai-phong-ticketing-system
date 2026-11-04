using System.Net;
using System.Text;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BetaGiaiPhong.Tests.IntegrationTests;

[Collection("DatabaseCollection")]
public sealed class MovieContentSyncTests(TestDatabaseFixture fixture)
{
    private sealed class StubClock(DateTime utcNow) : ISystemClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    [Fact]
    public async Task TC_SYNC_01_VerifiedShowcaseCatalog_SeedsAll31RealFilmsWithZeroFictionalEntries()
    {
        await using var db = await fixture.CreateDbContextAsync();

        var fictionalExists = await db.Movies.AnyAsync(m => m.Title.Contains("Phim mo phong"));
        Assert.False(fictionalExists);

        var verifiedImdbIds = MovieContentSyncService.VerifiedShowcaseCatalog.Select(x => x.ImdbId).ToList();
        Assert.Equal(31, verifiedImdbIds.Count);
        Assert.Equal(31, verifiedImdbIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var seededMovies = await db.Movies
            .Where(m => m.ImdbId != null && verifiedImdbIds.Contains(m.ImdbId))
            .ToListAsync();

        Assert.Equal(31, seededMovies.Count);

        var charade = seededMovies.Single(m => m.WikidataQid == "Q496266");
        Assert.Equal("Mê trong mê", charade.Title);
        Assert.Equal("Charade", charade.OriginalTitle);
        Assert.Equal((short)1963, charade.ReleaseYear);
        Assert.Equal("tt0056923", charade.ImdbId);
        Assert.Equal(4808, charade.TmdbId);
        Assert.Equal("IMDB", charade.RatingSource);
        Assert.Equal(7.8m, charade.RatingValue);
        Assert.Equal(95223, charade.RatingVoteCount);
        Assert.Equal("WIKIMEDIA_COMMONS", charade.ArtworkSource);
        Assert.True(charade.IsHeroEligible);
        Assert.Null(charade.AgeLabel);

        var diaDao = seededMovies.Single(m => m.WikidataQid == "Q124694260");
        Assert.Equal("Địa Đạo: Mặt Trời Trong Bóng Tối", diaDao.Title);
        Assert.Equal((short)2025, diaDao.ReleaseYear);
        Assert.Equal("T16", diaDao.AgeLabel);
        Assert.Equal("TMDB", diaDao.ArtworkSource);
        Assert.True(diaDao.IsHeroEligible);

        var duneTwo = seededMovies.Single(m => m.WikidataQid == "Q109228991");
        Assert.Equal("Dune: Hành Tinh Cát — Phần Hai", duneTwo.Title);
        Assert.Equal((short)2024, duneTwo.ReleaseYear);
        Assert.Equal(8.5m, duneTwo.RatingValue);
        Assert.True(duneTwo.IsHeroEligible);

        var latMat7 = seededMovies.Single(m => m.WikidataQid == "Q124984311");
        Assert.Equal("K", latMat7.AgeLabel);
        Assert.Equal("VN", latMat7.AgeLabelMarket);

        var latMat8 = seededMovies.Single(m => m.ImdbId == "tt36808524");
        Assert.Equal("Lật Mặt 8: Vòng Tay Nắng", latMat8.Title);
        Assert.Equal(1449973, latMat8.TmdbId);
        Assert.True(latMat8.IsHeroEligible);

        var mai = seededMovies.Single(m => m.WikidataQid == "Q124516759");
        Assert.Equal("Mai", mai.Title);
        Assert.Equal("T18", mai.AgeLabel);
        Assert.Equal("VN", mai.AgeLabelMarket);
        Assert.Equal("PENDING_TMDB_API_KEY", mai.ArtworkSource);
        Assert.Null(mai.PosterUrl);
        Assert.Null(mai.BackdropUrl);
        Assert.False(mai.IsHeroEligible);

        var daoPho = seededMovies.Single(m => m.WikidataQid == "Q124620657");
        Assert.Equal("T13", daoPho.AgeLabel);
        Assert.Equal("VN", daoPho.AgeLabelMarket);
        Assert.Equal("PENDING_TMDB_API_KEY", daoPho.ArtworkSource);
        Assert.False(daoPho.IsHeroEligible);
    }

    [Fact]
    public async Task TC_SYNC_02_TmdbMissingApiKey_ReturnsGracefulOutcomeWithoutMutatingCachedData()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerId = await db.Users
            .Where(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID")
            .Select(u => u.UserId)
            .SingleAsync();

        var charadeBefore = await db.Movies.AsNoTracking().SingleAsync(m => m.TmdbId == 4808);

        var options = new MovieContentSyncOptions { TmdbReadToken = null };
        var syncService = new MovieContentSyncService(db, new StubClock(new DateTime(2026, 11, 3, 6, 0, 0, DateTimeKind.Utc)), options);

        var result = await syncService.SyncMovieFromTmdbAsync(4808, managerId);
        Assert.Equal("MISSING_API_KEY", result.Outcome);
        Assert.Equal(charadeBefore.MovieId, result.MovieId);

        var charadeAfter = await db.Movies.AsNoTracking().SingleAsync(m => m.TmdbId == 4808);
        Assert.Equal(charadeBefore.Title, charadeAfter.Title);
        Assert.Equal(charadeBefore.PosterUrl, charadeAfter.PosterUrl);
        Assert.Equal(charadeBefore.BackdropUrl, charadeAfter.BackdropUrl);
        Assert.Equal(charadeBefore.RatingValue, charadeAfter.RatingValue);
    }

    [Fact]
    public async Task TC_SYNC_03_TmdbExternalError_PreservesCachedArtworkAndNeverLeaksBearerSecret()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerId = await db.Users
            .Where(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID")
            .Select(u => u.UserId)
            .SingleAsync();

        var charadeBefore = await db.Movies.AsNoTracking().SingleAsync(m => m.TmdbId == 4808);
        const string fakeSecret = "SUPER_SECRET_TMDB_BEARER_DO_NOT_LEAK_999";

        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("Upstream gateway timeout")
        });
        using var httpClient = new HttpClient(handler);

        var options = new MovieContentSyncOptions
        {
            TmdbReadToken = fakeSecret,
            TmdbBaseUrl = "https://tmdb.example.test/3/"
        };
        var syncService = new MovieContentSyncService(
            db,
            new StubClock(new DateTime(2026, 11, 3, 6, 30, 0, DateTimeKind.Utc)),
            options,
            httpClient);

        var result = await syncService.SyncMovieFromTmdbAsync(4808, managerId);
        Assert.Equal("EXTERNAL_ERROR", result.Outcome);
        Assert.DoesNotContain(fakeSecret, result.Message, StringComparison.Ordinal);

        var charadeAfter = await db.Movies.AsNoTracking().SingleAsync(m => m.TmdbId == 4808);
        Assert.Equal(charadeBefore.PosterUrl, charadeAfter.PosterUrl);
        Assert.Equal(charadeBefore.BackdropUrl, charadeAfter.BackdropUrl);
        Assert.Equal(charadeBefore.Synopsis, charadeAfter.Synopsis);

        var latestAudit = await db.AuditLogs
            .OrderByDescending(a => a.AuditId)
            .FirstAsync(a => a.EventType == "MOVIE_SYNC_FAILED");
        Assert.DoesNotContain(fakeSecret, latestAudit.DetailsJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TC_SYNC_04_TmdbImportAndManualOverrideMask_EnforcesDraft180DayTtlNoUsAgeConversionAndOverrideProtection()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerId = await db.Users
            .Where(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID")
            .Select(u => u.UserId)
            .SingleAsync();

        const int testTmdbId = 9988771;
        const string testImdbId = "tt9988771";
        long? createdMovieId = null;

        try
        {
            var initialSyncTime = new DateTime(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc);
            var secondSyncTime = new DateTime(2026, 11, 10, 7, 0, 0, DateTimeKind.Utc);

            string payloadV1 = """
            {
              "id": 9988771,
              "imdb_id": "tt9988771",
              "title": "",
              "original_title": "Metropolis Restored",
              "overview": "",
              "runtime": 148,
              "release_date": "1927-01-10",
              "original_language": "de",
              "origin_country": ["DE"],
              "poster_path": "/metropolis-v1-poster.jpg",
              "backdrop_path": "/metropolis-v1-backdrop.jpg",
              "vote_average": 8.3,
              "vote_count": 2450,
              "certification": "PG-13",
              "genres": [{"id": 878, "name": "Khoa học viễn tưởng"}],
              "credits": {
                "crew": [{"job": "Director", "name": "Fritz Lang"}],
                "cast": [{"name": "Brigitte Helm", "character": "Maria"}]
              },
              "videos": {
                "results": [{"site": "YouTube", "type": "Trailer", "official": true, "key": "gdtZv3XROnc"}]
              }
            }
            """;

            string payloadEnFallback = """
            {
              "id": 9988771,
              "overview": "In a futuristic city sharply divided between the working class and the city planners, the son of the city's mastermind falls in love with a working-class prophet."
            }
            """;

            string payloadV2 = """
            {
              "id": 9988771,
              "imdb_id": "tt9988771",
              "title": "Metropolis Bản Mới Từ TMDB",
              "original_title": "Metropolis Restored",
              "overview": "Tóm tắt mới từ TMDB không được phép ghi đè bản quản lý đã khóa.",
              "runtime": 153,
              "release_date": "1927-01-10",
              "original_language": "de",
              "origin_country": ["DE"],
              "poster_path": "/metropolis-v2-poster.jpg",
              "backdrop_path": "/metropolis-v2-backdrop.jpg",
              "vote_average": 8.4,
              "vote_count": 2600,
              "genres": [{"id": 878, "name": "Khoa học viễn tưởng"}, {"id": 18, "name": "Chính kịch"}],
              "credits": {
                "crew": [{"job": "Director", "name": "Fritz Lang"}],
                "cast": [
                  {"name": "Brigitte Helm", "character": "Maria"},
                  {"name": "Alfred Abel", "character": "Joh Fredersen"}
                ]
              },
              "videos": {
                "results": [{"site": "YouTube", "type": "Trailer", "official": true, "key": "NEW_TRAILER_ID"}]
              }
            }
            """;

            int phase = 1;
            using var handler = new StubHttpMessageHandler(req =>
            {
                var query = req.RequestUri?.Query ?? string.Empty;
                string json = phase == 1
                    ? (query.Contains("en-US", StringComparison.OrdinalIgnoreCase) ? payloadEnFallback : payloadV1)
                    : payloadV2;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            });
            using var httpClient = new HttpClient(handler);
            var options = new MovieContentSyncOptions
            {
                TmdbReadToken = "TEST_ONLY_NON_SECRET_TOKEN",
                TmdbBaseUrl = "https://tmdb.example.test/3/",
                TmdbImageBaseUrl = "https://image.tmdb.example.test/t/p/"
            };

            var syncV1 = new MovieContentSyncService(db, new StubClock(initialSyncTime), options, httpClient);
            var importRes = await syncV1.SyncMovieFromTmdbAsync(testTmdbId, managerId);

            Assert.Equal("CREATED_DRAFT", importRes.Outcome);
            Assert.NotNull(importRes.MovieId);
            createdMovieId = importRes.MovieId;

            var imported = await db.Movies.AsNoTracking().SingleAsync(m => m.MovieId == createdMovieId.Value);
            Assert.Equal("DRAFT", imported.Status);
            Assert.Null(imported.AgeLabel);
            Assert.Null(imported.AgeLabelMarket);
            Assert.Equal(initialSyncTime.AddDays(180), imported.ExternalExpiresUtc);
            Assert.Equal("Fritz Lang", imported.DirectorNames);
            Assert.Equal("gdtZv3XROnc", imported.OfficialTrailerYoutubeId);
            Assert.Contains("Tóm tắt gốc tiếng Anh từ TMDB", imported.Synopsis ?? string.Empty);

            // Manager edits Title, AgeLabel (VN T16), and Synopsis -> locks them in ManualOverrideMask
            var movieMgmt = new MovieManagementService(db, new StubClock(initialSyncTime.AddHours(1)));
            await movieMgmt.UpdateMovieAsync(
                managerId,
                createdMovieId.Value,
                "Đô Thị Tương Lai Metropolis (Bản Phục Chế)",
                148,
                imported.GenreText,
                "T16",
                "Bản dịch tóm tắt tiếng Việt đã được Quản lý rạp Beta Giải Phóng hiệu đính chính thức.",
                imported.PosterUrl);

            // Second TMDB sync runs later: locked fields stay intact; unlocked fields update!
            phase = 2;
            var syncV2 = new MovieContentSyncService(db, new StubClock(secondSyncTime), options, httpClient);
            var updateRes = await syncV2.SyncMovieFromTmdbAsync(testTmdbId, managerId);
            Assert.Equal("UPDATED", updateRes.Outcome);

            var afterResync = await db.Movies.AsNoTracking().SingleAsync(m => m.MovieId == createdMovieId.Value);
            Assert.Equal("Đô Thị Tương Lai Metropolis (Bản Phục Chế)", afterResync.Title);
            Assert.Equal("T16", afterResync.AgeLabel);
            Assert.Equal("VN", afterResync.AgeLabelMarket);
            Assert.Equal("Bản dịch tóm tắt tiếng Việt đã được Quản lý rạp Beta Giải Phóng hiệu đính chính thức.", afterResync.Synopsis);
            Assert.Equal((short)153, afterResync.DurationMin);
            Assert.Equal("NEW_TRAILER_ID", afterResync.OfficialTrailerYoutubeId);
            Assert.Contains("Alfred Abel", afterResync.CastJson ?? string.Empty);
            Assert.Equal(secondSyncTime.AddDays(180), afterResync.ExternalExpiresUtc);

            // Also verify IMDb TSV stream updates unlocked rating and skips locked rating
            var tsvContent = $"tconst\taverageRating\tnumVotes\n{testImdbId}\t8.3\t186400\ntt0000000\t0.0\t0\n";
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(tsvContent)))
            {
                var imdbSummary = await syncV2.ApplyImdbRatingsStreamAsync(ms, secondSyncTime);
                Assert.Equal(1, imdbSummary.MatchedMovies);
                Assert.Equal(1, imdbSummary.UpdatedMovies);
            }

            var afterImdb = await db.Movies.SingleAsync(m => m.MovieId == createdMovieId.Value);
            Assert.Equal("IMDB", afterImdb.RatingSource);
            Assert.Equal(8.3m, afterImdb.RatingValue);
            Assert.Equal(186400, afterImdb.RatingVoteCount);

            // Lock rating_value and verify next TSV stream skips it
            afterImdb.AddManualOverrides("rating_value");
            await db.SaveChangesAsync();

            var tsvContent2 = $"tconst\taverageRating\tnumVotes\n{testImdbId}\t5.1\t999999\n";
            using (var ms2 = new MemoryStream(Encoding.UTF8.GetBytes(tsvContent2)))
            {
                var imdbSummary2 = await syncV2.ApplyImdbRatingsStreamAsync(ms2, secondSyncTime.AddDays(1));
                Assert.Equal(1, imdbSummary2.MatchedMovies);
                Assert.Equal(0, imdbSummary2.UpdatedMovies);
                Assert.Equal(1, imdbSummary2.SkippedManualOverrides);
            }

            var afterLockedImdb = await db.Movies.AsNoTracking().SingleAsync(m => m.MovieId == createdMovieId.Value);
            Assert.Equal(8.3m, afterLockedImdb.RatingValue);
            Assert.Equal(186400, afterLockedImdb.RatingVoteCount);
        }
        finally
        {
            if (createdMovieId.HasValue)
            {
                var toRemove = await db.Movies.SingleOrDefaultAsync(m => m.MovieId == createdMovieId.Value);
                if (toRemove is not null)
                {
                    db.Movies.Remove(toRemove);
                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
