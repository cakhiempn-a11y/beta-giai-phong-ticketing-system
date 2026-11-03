using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Services;

public sealed class MovieContentSyncOptions
{
    public string? TmdbReadToken { get; set; }
    public string? MediaCacheDirectory { get; set; }
    public string TmdbBaseUrl { get; set; } = "https://api.themoviedb.org/3/";
    public string TmdbImageBaseUrl { get; set; } = "https://image.tmdb.org/t/p/";
    public string UserAgent { get; set; } = "BetaGiaiPhongAcademicTicketing/2.0";

    public static MovieContentSyncOptions FromEnvironment() => new()
    {
        TmdbReadToken = Environment.GetEnvironmentVariable("BGP_TMDB_READ_TOKEN"),
        MediaCacheDirectory = Environment.GetEnvironmentVariable("BGP_MEDIA_CACHE_DIR")
    };
}

public sealed record MovieCreditSeedItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("character")] string? Character);

public sealed record VerifiedMovieSeedEntry(
    string Key,
    string WikidataQid,
    string ImdbId,
    int TmdbId,
    string Title,
    string OriginalTitle,
    short ReleaseYear,
    DateOnly? ReleaseDateVn,
    short DurationMin,
    string OriginCountry,
    string OriginalLanguage,
    string GenreText,
    string DirectorNames,
    IReadOnlyList<MovieCreditSeedItem> Cast,
    string Synopsis,
    string? PosterUrl,
    string? PosterW342Url,
    string? BackdropUrl,
    string? BackdropW780Url,
    string? TitleLogoUrl,
    string? OfficialTrailerYoutubeId,
    string ArtworkSource,
    string? ArtworkLicenseText,
    string? ArtworkAttributionText,
    decimal? RatingValue,
    int? RatingVoteCount,
    string? AgeLabelVn,
    string? AgeLabelSource,
    bool HasDefaultShowcaseShowtime);

public sealed record CatalogSyncSummary(
    int CreatedCount,
    int UpdatedCount,
    int PreservedOverrideFieldsCount,
    IReadOnlyList<long> MovieIds);

public sealed record ExternalMovieSyncResult(
    string Outcome,
    long? MovieId,
    string Message);

public sealed record ExternalMovieLookupItem(
    string SourceKind,
    string? VerifiedKey,
    long? ExistingMovieId,
    string? ExistingStatus,
    string? WikidataQid,
    string? ImdbId,
    int? TmdbId,
    string Title,
    string? OriginalTitle,
    short? ReleaseYear,
    short DurationMin,
    string? GenreText,
    string? DirectorNames,
    string? OriginCountry,
    string? OriginalLanguage,
    string? Synopsis,
    string? PosterUrl,
    string? PosterW342Url,
    string? BackdropUrl,
    string? OfficialTrailerYoutubeId,
    string? ArtworkSource,
    string? ArtworkLicenseText,
    string? RatingSource,
    decimal? RatingValue,
    int? RatingVoteCount,
    string? AgeLabelVn);

public sealed record ExternalMovieLookupResponse(
    bool IsTmdbConfigured,
    string Query,
    string StatusCode,
    string StatusMessage,
    IReadOnlyList<ExternalMovieLookupItem> Items);

public sealed record ImdbRatingsSyncSummary(
    int MatchedMovies,
    int UpdatedMovies,
    int SkippedManualOverrides,
    long RowsScanned);

public sealed class MovieContentSyncService
{
    private static readonly HttpClient SharedHttpClient = CreateDefaultHttpClient();
    private static readonly DateTime VerifiedDatasetFetchedUtc = new(2026, 11, 3, 3, 47, 17, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly BetaDbContext _db;
    private readonly ISystemClock _clock;
    private readonly MovieContentSyncOptions _options;
    private readonly HttpClient _httpClient;

    public MovieContentSyncService(
        BetaDbContext db,
        ISystemClock clock,
        MovieContentSyncOptions? options = null,
        HttpClient? httpClient = null)
    {
        _db = db;
        _clock = clock;
        _options = options ?? MovieContentSyncOptions.FromEnvironment();
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public bool IsTmdbConfigured => !string.IsNullOrWhiteSpace(_options.TmdbReadToken);

    public static IReadOnlyList<VerifiedMovieSeedEntry> VerifiedShowcaseCatalog { get; } = BuildVerifiedCatalog();

    public static IReadOnlyList<VerifiedMovieSeedEntry> VerifiedImportableCatalog { get; } = BuildImportableVerifiedCatalog();

    public static IReadOnlyList<VerifiedMovieSeedEntry> AllVerifiedEntries { get; } =
        VerifiedShowcaseCatalog.Concat(VerifiedImportableCatalog).ToList();

    public async Task<ExternalMovieLookupResponse> LookupExternalMoviesAsync(
        string? rawQuery,
        CancellationToken ct = default)
    {
        var query = rawQuery?.Trim() ?? string.Empty;
        var dbMovies = await _db.Movies
            .AsNoTracking()
            .Select(m => new { m.MovieId, m.Status, m.WikidataQid, m.ImdbId, m.TmdbId })
            .ToListAsync(ct);

        (long? MovieId, string? Status) ResolveExisting(string? qid, string? imdbId, int? tmdbId)
        {
            var hit = dbMovies.FirstOrDefault(m =>
                (!string.IsNullOrWhiteSpace(qid) && !string.IsNullOrWhiteSpace(m.WikidataQid) &&
                 string.Equals(m.WikidataQid, qid, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(imdbId) && !string.IsNullOrWhiteSpace(m.ImdbId) &&
                 string.Equals(m.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase)) ||
                (tmdbId.HasValue && m.TmdbId.HasValue && m.TmdbId.Value == tmdbId.Value));

            return hit is null ? (null, null) : (hit.MovieId, hit.Status);
        }

        ExternalMovieLookupItem MapVerified(VerifiedMovieSeedEntry entry)
        {
            var (existingId, existingStatus) = ResolveExisting(entry.WikidataQid, entry.ImdbId, entry.TmdbId);
            return new ExternalMovieLookupItem(
                SourceKind: "VERIFIED_CATALOG",
                VerifiedKey: entry.Key,
                ExistingMovieId: existingId,
                ExistingStatus: existingStatus,
                WikidataQid: entry.WikidataQid,
                ImdbId: entry.ImdbId,
                TmdbId: entry.TmdbId,
                Title: entry.Title,
                OriginalTitle: entry.OriginalTitle,
                ReleaseYear: entry.ReleaseYear,
                DurationMin: entry.DurationMin,
                GenreText: entry.GenreText,
                DirectorNames: entry.DirectorNames,
                OriginCountry: entry.OriginCountry,
                OriginalLanguage: entry.OriginalLanguage,
                Synopsis: entry.Synopsis,
                PosterUrl: entry.PosterUrl,
                PosterW342Url: entry.PosterW342Url,
                BackdropUrl: entry.BackdropUrl,
                OfficialTrailerYoutubeId: entry.OfficialTrailerYoutubeId,
                ArtworkSource: entry.ArtworkSource,
                ArtworkLicenseText: entry.ArtworkLicenseText,
                RatingSource: entry.RatingValue.HasValue ? "IMDB" : null,
                RatingValue: entry.RatingValue,
                RatingVoteCount: entry.RatingVoteCount,
                AgeLabelVn: entry.AgeLabelVn);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            var orderedDefaults = VerifiedImportableCatalog
                .Concat(VerifiedShowcaseCatalog)
                .Select(MapVerified)
                .OrderBy(i => i.ExistingMovieId.HasValue ? 1 : 0)
                .ThenByDescending(i => i.ReleaseYear)
                .ToList();

            var defaultMsg = IsTmdbConfigured
                ? "Đang kết nối TMDB API trực tiếp + Danh mục xác minh Wikidata/IMDb nội bộ."
                : "Chưa cấu hình BGP_TMDB_READ_TOKEN — đang hiển thị danh mục tác phẩm xác minh Wikidata / IMDb / Blender Open Movie sẵn sàng nhập hoặc đồng bộ.";

            return new ExternalMovieLookupResponse(
                IsTmdbConfigured,
                string.Empty,
                "OK",
                defaultMsg,
                orderedDefaults);
        }

        var normalizedQuery = query.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase)
            ? query.Substring(5).Trim()
            : query;
        bool parsedTmdbId = int.TryParse(normalizedQuery, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericTmdbId) && numericTmdbId > 0;

        var results = new List<ExternalMovieLookupItem>();
        foreach (var entry in AllVerifiedEntries)
        {
            bool matched =
                string.Equals(entry.Key, query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.WikidataQid, query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.ImdbId, query, StringComparison.OrdinalIgnoreCase) ||
                (parsedTmdbId && entry.TmdbId == numericTmdbId) ||
                entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.OriginalTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.DirectorNames.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (matched)
            {
                results.Add(MapVerified(entry));
            }
        }

        var token = _options.TmdbReadToken?.Trim();
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                if (parsedTmdbId)
                {
                    using var tmdbDoc = await FetchTmdbMovieJsonAsync(numericTmdbId, "vi-VN", token, ct);
                    if (tmdbDoc is not null)
                    {
                        var liveItem = MapTmdbRootToLookupItem(numericTmdbId, tmdbDoc.RootElement, ResolveExisting);
                        results.RemoveAll(r => r.TmdbId == numericTmdbId);
                        results.Insert(0, liveItem);
                    }
                }
                else
                {
                    using var searchDoc = await SearchTmdbMoviesJsonAsync(normalizedQuery, token, ct);
                    if (searchDoc is not null &&
                        searchDoc.RootElement.TryGetProperty("results", out var arr) &&
                        arr.ValueKind == JsonValueKind.Array)
                    {
                        int added = 0;
                        foreach (var itemEl in arr.EnumerateArray())
                        {
                            if (!itemEl.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var hitTmdbId) || hitTmdbId <= 0)
                            {
                                continue;
                            }
                            if (results.Any(r => r.TmdbId == hitTmdbId))
                            {
                                continue;
                            }

                            var releaseDate = GetTrimmedString(itemEl, "release_date");
                            short? year = null;
                            if (!string.IsNullOrWhiteSpace(releaseDate) &&
                                releaseDate.Length >= 4 &&
                                short.TryParse(releaseDate.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYr))
                            {
                                year = parsedYr;
                            }

                            var imageBase = _options.TmdbImageBaseUrl.EndsWith("/", StringComparison.Ordinal)
                                ? _options.TmdbImageBaseUrl
                                : _options.TmdbImageBaseUrl + "/";
                            var posterPath = NormalizeImagePath(GetTrimmedString(itemEl, "poster_path"));
                            var backdropPath = NormalizeImagePath(GetTrimmedString(itemEl, "backdrop_path"));
                            var (exId, exStatus) = ResolveExisting(null, null, hitTmdbId);

                            decimal? ratingVal = null;
                            int? voteCnt = null;
                            if (itemEl.TryGetProperty("vote_average", out var va) && va.TryGetDecimal(out var dVal) &&
                                itemEl.TryGetProperty("vote_count", out var vc) && vc.TryGetInt32(out var iCnt) && iCnt > 0)
                            {
                                ratingVal = Math.Round(dVal, 1, MidpointRounding.AwayFromZero);
                                voteCnt = iCnt;
                            }

                            var origTitle = GetTrimmedString(itemEl, "original_title");
                            var title = GetTrimmedString(itemEl, "title") ?? origTitle ?? $"TMDB #{hitTmdbId}";

                            results.Add(new ExternalMovieLookupItem(
                                SourceKind: "TMDB_LIVE",
                                VerifiedKey: null,
                                ExistingMovieId: exId,
                                ExistingStatus: exStatus,
                                WikidataQid: null,
                                ImdbId: null,
                                TmdbId: hitTmdbId,
                                Title: title,
                                OriginalTitle: origTitle,
                                ReleaseYear: year,
                                DurationMin: 100,
                                GenreText: null,
                                DirectorNames: null,
                                OriginCountry: null,
                                OriginalLanguage: GetTrimmedString(itemEl, "original_language"),
                                Synopsis: GetTrimmedString(itemEl, "overview"),
                                PosterUrl: posterPath is not null ? $"{imageBase}w780{posterPath}" : null,
                                PosterW342Url: posterPath is not null ? $"{imageBase}w342{posterPath}" : null,
                                BackdropUrl: backdropPath is not null ? $"{imageBase}w1280{backdropPath}" : null,
                                OfficialTrailerYoutubeId: null,
                                ArtworkSource: posterPath is not null || backdropPath is not null ? "TMDB" : "NONE",
                                ArtworkLicenseText: "TMDB API Non-Commercial Attribution",
                                RatingSource: ratingVal.HasValue ? "TMDB" : null,
                                RatingValue: ratingVal,
                                RatingVoteCount: voteCnt,
                                AgeLabelVn: null));

                            added++;
                            if (added >= 5) break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return new ExternalMovieLookupResponse(
                    IsTmdbConfigured: true,
                    Query: query,
                    StatusCode: "EXTERNAL_ERROR",
                    StatusMessage: $"Không thể kết nối tới TMDB API ({ex.GetType().Name}). Đang hiển thị kết quả từ danh mục xác minh nội bộ (nếu có).",
                    Items: results);
            }
        }

        if (results.Count == 0 && !IsTmdbConfigured)
        {
            return new ExternalMovieLookupResponse(
                IsTmdbConfigured: false,
                Query: query,
                StatusCode: "TMDB_TOKEN_MISSING",
                StatusMessage: $"Không tìm thấy \"{query}\" trong danh mục xác minh nội bộ và máy chủ chưa cấu hình khóa BGP_TMDB_READ_TOKEN để truy vấn trực tiếp TMDB API. Hãy thử tra cứu bằng mã xác minh có sẵn (ví dụ: tt0807840, Q737719, 39079, Q731114) hoặc cấu hình BGP_TMDB_READ_TOKEN trong môi trường máy chủ.",
                Items: results);
        }

        if (results.Count == 0)
        {
            return new ExternalMovieLookupResponse(
                IsTmdbConfigured,
                query,
                "NO_MATCH",
                $"Không tìm thấy tác phẩm nào khớp với từ khóa hoặc định danh \"{query}\".",
                results);
        }

        return new ExternalMovieLookupResponse(
            IsTmdbConfigured,
            query,
            "OK",
            $"Tìm thấy {results.Count} tác phẩm xác minh khớp với \"{query}\".",
            results);
    }

    public async Task<ExternalMovieSyncResult> SyncExternalMovieAsync(
        string? verifiedKey,
        int? tmdbId,
        long? actorUserId = null,
        long? targetMovieId = null,
        CancellationToken ct = default)
    {
        if (IsTmdbConfigured && tmdbId is > 0 && string.IsNullOrWhiteSpace(verifiedKey))
        {
            return await SyncMovieFromTmdbAsync(tmdbId.Value, actorUserId, ct);
        }

        VerifiedMovieSeedEntry? verifiedSeed = null;
        if (!string.IsNullOrWhiteSpace(verifiedKey))
        {
            verifiedSeed = AllVerifiedEntries.FirstOrDefault(e =>
                string.Equals(e.Key, verifiedKey.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.WikidataQid, verifiedKey.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.ImdbId, verifiedKey.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (verifiedSeed is null && tmdbId is > 0)
        {
            if (IsTmdbConfigured)
            {
                return await SyncMovieFromTmdbAsync(tmdbId.Value, actorUserId, ct);
            }

            verifiedSeed = AllVerifiedEntries.FirstOrDefault(e => e.TmdbId == tmdbId.Value);
        }

        if (verifiedSeed is not null)
        {
            Movie? existing = null;
            if (targetMovieId is > 0)
            {
                existing = await _db.Movies.FirstOrDefaultAsync(m => m.MovieId == targetMovieId.Value, ct);
            }

            existing ??= await _db.Movies.FirstOrDefaultAsync(m =>
                (m.WikidataQid != null && m.WikidataQid == verifiedSeed.WikidataQid) ||
                (m.ImdbId != null && m.ImdbId == verifiedSeed.ImdbId) ||
                (m.TmdbId.HasValue && m.TmdbId.Value == verifiedSeed.TmdbId), ct);

            bool isNew = existing is null;
            var movie = existing ?? new Movie
            {
                Status = "DRAFT"
            };

            var nowUtc = _clock.UtcNow;
            var preserved = ApplyVerifiedSeedToEntity(movie, verifiedSeed, nowUtc);

            if (isNew)
            {
                _db.Movies.Add(movie);
            }

            await _db.SaveChangesAsync(ct);

            if (actorUserId.HasValue)
            {
                _db.AuditLogs.Add(Auditing.Event(
                    actorUserId.Value,
                    isNew ? "MOVIE_IMPORTED_VERIFIED" : "MOVIE_SYNCED_VERIFIED",
                    "Movie",
                    movie.MovieId.ToString(CultureInfo.InvariantCulture),
                    new
                    {
                        movie.MovieId,
                        verifiedSeed.Key,
                        verifiedSeed.WikidataQid,
                        verifiedSeed.ImdbId,
                        verifiedSeed.TmdbId,
                        PreservedOverrides = preserved,
                        movie.Status
                    },
                    nowUtc));
                await _db.SaveChangesAsync(ct);
            }

            return new ExternalMovieSyncResult(
                isNew ? "CREATED_DRAFT" : "UPDATED",
                movie.MovieId,
                isNew
                    ? $"Đã nhập phim '{movie.Title}' ({verifiedSeed.WikidataQid} / {verifiedSeed.ImdbId}) ở trạng thái DRAFT. Vui lòng rà soát nhãn độ tuổi Việt Nam trước khi xuất bản."
                    : $"Đã đồng bộ lại dữ liệu nguồn xác minh cho '{movie.Title}' (giữ nguyên {preserved} trường đã khóa thủ công).");
        }

        if (tmdbId is > 0)
        {
            return await SyncMovieFromTmdbAsync(tmdbId.Value, actorUserId, ct);
        }

        return new ExternalMovieSyncResult(
            "INVALID_ID",
            null,
            "Vui lòng cung cấp mã định danh tác phẩm hợp lệ (TMDB ID, IMDb ID hoặc Wikidata QID).");
    }

    public async Task<CatalogSyncSummary> SyncVerifiedShowcaseCatalogAsync(
        bool publishAll = true,
        long? actorUserId = null,
        CancellationToken ct = default)
    {
        var existingMovies = await _db.Movies.ToListAsync(ct);
        int created = 0;
        int updated = 0;
        int preservedFields = 0;
        var syncedEntries = new List<Movie>();
        var nowUtc = _clock.UtcNow;

        foreach (var seed in VerifiedShowcaseCatalog)
        {
            var match = existingMovies.FirstOrDefault(m =>
                (!string.IsNullOrWhiteSpace(m.WikidataQid) && string.Equals(m.WikidataQid, seed.WikidataQid, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(m.ImdbId) && string.Equals(m.ImdbId, seed.ImdbId, StringComparison.OrdinalIgnoreCase)) ||
                (m.TmdbId.HasValue && m.TmdbId.Value == seed.TmdbId));

            bool isNew = match is null;
            if (match is null)
            {
                match = new Movie
                {
                    Status = publishAll ? "PUBLISHED" : "DRAFT"
                };
                _db.Movies.Add(match);
                existingMovies.Add(match);
                created++;
            }
            else
            {
                if (publishAll && string.Equals(match.Status, "DRAFT", StringComparison.OrdinalIgnoreCase) && !match.HasManualOverride("status"))
                {
                    match.Status = "PUBLISHED";
                }
                updated++;
            }

            preservedFields += ApplyVerifiedSeedToEntity(match, seed, nowUtc);
            syncedEntries.Add(match);
        }

        await _db.SaveChangesAsync(ct);

        var ids = syncedEntries.Select(m => m.MovieId).ToList();
        if (actorUserId.HasValue)
        {
            _db.AuditLogs.Add(Auditing.Event(
                actorUserId.Value,
                "VERIFIED_CATALOG_SYNCED",
                "MovieCatalog",
                "VERIFIED_11",
                new { Created = created, Updated = updated, PreservedOverrides = preservedFields, Total = ids.Count },
                nowUtc));
            await _db.SaveChangesAsync(ct);
        }

        return new CatalogSyncSummary(created, updated, preservedFields, ids);
    }

    public async Task<ExternalMovieSyncResult> SyncMovieFromTmdbAsync(
        int tmdbId,
        long? actorUserId = null,
        CancellationToken ct = default)
    {
        if (tmdbId <= 0)
        {
            return new ExternalMovieSyncResult("INVALID_ID", null, "Mã TMDB ID phải là số nguyên dương.");
        }

        var existing = await _db.Movies.FirstOrDefaultAsync(m => m.TmdbId == tmdbId, ct);
        var token = _options.TmdbReadToken?.Trim();
        var nowUtc = _clock.UtcNow;

        if (string.IsNullOrWhiteSpace(token))
        {
            if (actorUserId.HasValue)
            {
                _db.AuditLogs.Add(Auditing.Event(
                    actorUserId.Value,
                    "MOVIE_SYNC_SKIPPED_NO_KEY",
                    "Movie",
                    existing?.MovieId.ToString(CultureInfo.InvariantCulture) ?? $"tmdb:{tmdbId}",
                    new { TmdbId = tmdbId, Reason = "BGP_TMDB_READ_TOKEN is not configured" },
                    nowUtc));
                await _db.SaveChangesAsync(ct);
            }

            return new ExternalMovieSyncResult(
                "MISSING_API_KEY",
                existing?.MovieId,
                "Chưa cấu hình biến môi trường BGP_TMDB_READ_TOKEN. Giữ nguyên dữ liệu hiện có trong cơ sở dữ liệu.");
        }

        try
        {
            var viDoc = await FetchTmdbMovieJsonAsync(tmdbId, "vi-VN", token, ct);
            if (viDoc is null)
            {
                return new ExternalMovieSyncResult(
                    "NOT_FOUND",
                    existing?.MovieId,
                    $"Không tìm thấy tác phẩm với TMDB ID #{tmdbId} từ nguồn TMDB.");
            }

            using (viDoc)
            {
                var root = viDoc.RootElement;
                string? overview = GetTrimmedString(root, "overview");
                JsonDocument? enFallbackDoc = null;

                try
                {
                    if (string.IsNullOrWhiteSpace(overview))
                    {
                        enFallbackDoc = await FetchTmdbMovieJsonAsync(tmdbId, "en-US", token, ct);
                        if (enFallbackDoc is not null)
                        {
                            var enOverview = GetTrimmedString(enFallbackDoc.RootElement, "overview");
                            if (!string.IsNullOrWhiteSpace(enOverview))
                            {
                                overview = $"{enOverview} (Tóm tắt gốc tiếng Anh từ TMDB)";
                            }
                        }
                    }

                    var imdbId = NormalizeImdbId(GetTrimmedString(root, "imdb_id"));
                    if (existing is null && !string.IsNullOrWhiteSpace(imdbId))
                    {
                        existing = await _db.Movies.FirstOrDefaultAsync(m => m.ImdbId == imdbId, ct);
                    }

                    bool isNew = existing is null;
                    var movie = existing ?? new Movie
                    {
                        Status = "DRAFT"
                    };

                    ApplyTmdbPayloadToEntity(movie, tmdbId, imdbId, root, overview, nowUtc);

                    if (isNew)
                    {
                        _db.Movies.Add(movie);
                    }

                    await _db.SaveChangesAsync(ct);

                    if (actorUserId.HasValue)
                    {
                        _db.AuditLogs.Add(Auditing.Event(
                            actorUserId.Value,
                            isNew ? "MOVIE_IMPORTED_TMDB" : "MOVIE_SYNCED_TMDB",
                            "Movie",
                            movie.MovieId.ToString(CultureInfo.InvariantCulture),
                            new { movie.MovieId, TmdbId = tmdbId, movie.ImdbId, movie.Status, movie.ExternalExpiresUtc },
                            nowUtc));
                        await _db.SaveChangesAsync(ct);
                    }

                    return new ExternalMovieSyncResult(
                        isNew ? "CREATED_DRAFT" : "UPDATED",
                        movie.MovieId,
                        isNew
                            ? $"Đã nhập phim mới từ TMDB #{tmdbId} ở trạng thái DRAFT để quản trị viên kiểm duyệt."
                            : $"Đã đồng bộ dữ liệu mới nhất từ TMDB #{tmdbId}.");
                }
                finally
                {
                    enFallbackDoc?.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            var safeError = ex.GetType().Name;
            if (actorUserId.HasValue)
            {
                _db.AuditLogs.Add(Auditing.Event(
                    actorUserId.Value,
                    "MOVIE_SYNC_FAILED",
                    "Movie",
                    existing?.MovieId.ToString(CultureInfo.InvariantCulture) ?? $"tmdb:{tmdbId}",
                    new { TmdbId = tmdbId, ErrorType = safeError },
                    nowUtc));
                await _db.SaveChangesAsync(ct);
            }

            return new ExternalMovieSyncResult(
                "EXTERNAL_ERROR",
                existing?.MovieId,
                $"Không thể kết nối hoặc đọc phản hồi từ TMDB ({safeError}). Dữ liệu và hình ảnh hiện có trong DB được giữ nguyên an toàn.");
        }
    }

    public async Task<ImdbRatingsSyncSummary> ApplyImdbRatingsStreamAsync(
        Stream tsvStream,
        DateTime? fetchedUtc = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tsvStream);

        var moviesWithImdb = await _db.Movies
            .Where(m => m.ImdbId != null)
            .ToListAsync(ct);

        var byImdb = new Dictionary<string, Movie>(StringComparer.OrdinalIgnoreCase);
        foreach (var movie in moviesWithImdb)
        {
            if (!string.IsNullOrWhiteSpace(movie.ImdbId))
            {
                byImdb[movie.ImdbId.Trim()] = movie;
            }
        }

        int matched = 0;
        int updated = 0;
        int skippedOverrides = 0;
        long scanned = 0;
        var effectiveFetchedUtc = TruncateToSeconds(fetchedUtc ?? _clock.UtcNow);

        using var reader = new StreamReader(tsvStream, leaveOpen: true);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("tconst", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            scanned++;
            var parts = line.Split('\t');
            if (parts.Length < 3)
            {
                continue;
            }

            var tconst = parts[0].Trim();
            if (!byImdb.TryGetValue(tconst, out var movie))
            {
                continue;
            }

            matched++;
            if (movie.HasManualOverride("rating_value"))
            {
                skippedOverrides++;
                continue;
            }

            if (!decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var avgRating) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var votes) ||
                votes <= 0 ||
                avgRating < 0m ||
                avgRating > 10m)
            {
                continue;
            }

            movie.RatingSource = "IMDB";
            movie.RatingValue = Math.Round(avgRating, 1, MidpointRounding.AwayFromZero);
            movie.RatingScaleMax = 10.0m;
            movie.RatingVoteCount = votes;
            movie.RatingFetchedUtc = effectiveFetchedUtc;
            updated++;
        }

        if (updated > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return new ImdbRatingsSyncSummary(matched, updated, skippedOverrides, scanned);
    }

    private async Task<JsonDocument?> FetchTmdbMovieJsonAsync(
        int tmdbId,
        string language,
        string bearerToken,
        CancellationToken ct)
    {
        var baseUri = _options.TmdbBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? _options.TmdbBaseUrl
            : _options.TmdbBaseUrl + "/";

        var requestUri = $"{baseUri}movie/{tmdbId}?language={Uri.EscapeDataString(language)}&append_to_response=credits,videos";
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_options.UserAgent))
        {
            req.Headers.UserAgent.ParseAdd(_options.UserAgent);
        }

        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private async Task<JsonDocument?> SearchTmdbMoviesJsonAsync(
        string query,
        string bearerToken,
        CancellationToken ct)
    {
        var baseUri = _options.TmdbBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? _options.TmdbBaseUrl
            : _options.TmdbBaseUrl + "/";

        var requestUri = $"{baseUri}search/movie?query={Uri.EscapeDataString(query)}&language=vi-VN&include_adult=false";
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_options.UserAgent))
        {
            req.Headers.UserAgent.ParseAdd(_options.UserAgent);
        }

        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private ExternalMovieLookupItem MapTmdbRootToLookupItem(
        int tmdbId,
        JsonElement root,
        Func<string?, string?, int?, (long? MovieId, string? Status)> resolveExisting)
    {
        var imdbId = NormalizeImdbId(GetTrimmedString(root, "imdb_id"));
        var (existingId, existingStatus) = resolveExisting(null, imdbId, tmdbId);

        var origTitle = GetTrimmedString(root, "original_title");
        var title = GetTrimmedString(root, "title") ?? origTitle ?? $"TMDB #{tmdbId}";

        short durationMin = 100;
        if (root.TryGetProperty("runtime", out var rt) && rt.ValueKind == JsonValueKind.Number &&
            rt.TryGetInt32(out var rtMin) && rtMin is >= 1 and <= 480)
        {
            durationMin = (short)rtMin;
        }

        short? releaseYear = null;
        var relStr = GetTrimmedString(root, "release_date");
        if (!string.IsNullOrWhiteSpace(relStr) && relStr.Length >= 4 &&
            short.TryParse(relStr.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var yr))
        {
            releaseYear = yr;
        }

        string? genreText = null;
        if (root.TryGetProperty("genres", out var genresEl) && genresEl.ValueKind == JsonValueKind.Array)
        {
            var names = new List<string>();
            foreach (var g in genresEl.EnumerateArray())
            {
                var n = GetTrimmedString(g, "name");
                if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
            }
            if (names.Count > 0) genreText = TruncateLength(string.Join(", ", names), 160);
        }

        string? directorNames = null;
        if (root.TryGetProperty("credits", out var creditsEl) && creditsEl.ValueKind == JsonValueKind.Object &&
            creditsEl.TryGetProperty("crew", out var crewEl) && crewEl.ValueKind == JsonValueKind.Array)
        {
            var dirs = new List<string>();
            foreach (var c in crewEl.EnumerateArray())
            {
                if (string.Equals(GetTrimmedString(c, "job"), "Director", StringComparison.OrdinalIgnoreCase))
                {
                    var dn = GetTrimmedString(c, "name");
                    if (!string.IsNullOrWhiteSpace(dn) && !dirs.Contains(dn, StringComparer.OrdinalIgnoreCase))
                    {
                        dirs.Add(dn);
                    }
                }
            }
            if (dirs.Count > 0) directorNames = TruncateLength(string.Join(", ", dirs), 300);
        }

        string? originCountry = null;
        if (root.TryGetProperty("origin_country", out var ocEl) && ocEl.ValueKind == JsonValueKind.Array)
        {
            var codes = new List<string>();
            foreach (var c in ocEl.EnumerateArray())
            {
                var cd = c.GetString();
                if (!string.IsNullOrWhiteSpace(cd)) codes.Add(cd.Trim());
            }
            if (codes.Count > 0) originCountry = TruncateLength(string.Join(", ", codes), 100);
        }

        string? trailerKey = null;
        if (root.TryGetProperty("videos", out var vidsEl) && vidsEl.ValueKind == JsonValueKind.Object &&
            vidsEl.TryGetProperty("results", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in resEl.EnumerateArray())
            {
                if (string.Equals(GetTrimmedString(v, "site"), "YouTube", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(GetTrimmedString(v, "type"), "Trailer", StringComparison.OrdinalIgnoreCase))
                {
                    trailerKey = GetTrimmedString(v, "key");
                    if (!string.IsNullOrWhiteSpace(trailerKey)) break;
                }
            }
        }

        var imageBase = _options.TmdbImageBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? _options.TmdbImageBaseUrl
            : _options.TmdbImageBaseUrl + "/";
        var posterPath = NormalizeImagePath(GetTrimmedString(root, "poster_path"));
        var backdropPath = NormalizeImagePath(GetTrimmedString(root, "backdrop_path"));

        decimal? ratingVal = null;
        int? voteCnt = null;
        if (root.TryGetProperty("vote_average", out var vaEl) && vaEl.TryGetDecimal(out var va) &&
            root.TryGetProperty("vote_count", out var vcEl) && vcEl.TryGetInt32(out var vc) && vc > 0)
        {
            ratingVal = Math.Round(va, 1, MidpointRounding.AwayFromZero);
            voteCnt = vc;
        }

        return new ExternalMovieLookupItem(
            SourceKind: "TMDB_LIVE",
            VerifiedKey: null,
            ExistingMovieId: existingId,
            ExistingStatus: existingStatus,
            WikidataQid: null,
            ImdbId: imdbId,
            TmdbId: tmdbId,
            Title: title,
            OriginalTitle: origTitle,
            ReleaseYear: releaseYear,
            DurationMin: durationMin,
            GenreText: genreText,
            DirectorNames: directorNames,
            OriginCountry: originCountry,
            OriginalLanguage: GetTrimmedString(root, "original_language"),
            Synopsis: GetTrimmedString(root, "overview"),
            PosterUrl: posterPath is not null ? $"{imageBase}w780{posterPath}" : null,
            PosterW342Url: posterPath is not null ? $"{imageBase}w342{posterPath}" : null,
            BackdropUrl: backdropPath is not null ? $"{imageBase}w1280{backdropPath}" : null,
            OfficialTrailerYoutubeId: trailerKey,
            ArtworkSource: posterPath is not null || backdropPath is not null ? "TMDB" : "NONE",
            ArtworkLicenseText: "TMDB API Non-Commercial Attribution",
            RatingSource: ratingVal.HasValue ? "TMDB" : null,
            RatingValue: ratingVal,
            RatingVoteCount: voteCnt,
            AgeLabelVn: null);
    }

    private void ApplyTmdbPayloadToEntity(
        Movie movie,
        int tmdbId,
        string? imdbId,
        JsonElement root,
        string? resolvedOverview,
        DateTime nowUtc)
    {
        var syncedUtc = TruncateToSeconds(nowUtc);
        movie.TmdbId = tmdbId;
        if (!string.IsNullOrWhiteSpace(imdbId) && string.IsNullOrWhiteSpace(movie.ImdbId))
        {
            movie.ImdbId = imdbId;
        }

        var originalTitle = GetTrimmedString(root, "original_title");
        var localizedTitle = GetTrimmedString(root, "title") ?? originalTitle ?? $"TMDB #{tmdbId}";

        if (!movie.HasManualOverride("title"))
        {
            movie.Title = TruncateLength(localizedTitle, 250);
        }

        if (!movie.HasManualOverride("original_title") && !string.IsNullOrWhiteSpace(originalTitle))
        {
            movie.OriginalTitle = TruncateLength(originalTitle, 250);
        }

        if (!movie.HasManualOverride("duration_min"))
        {
            if (root.TryGetProperty("runtime", out var runtimeProp) &&
                runtimeProp.ValueKind == JsonValueKind.Number &&
                runtimeProp.TryGetInt32(out var runtimeMin) &&
                runtimeMin is >= 1 and <= 480)
            {
                movie.DurationMin = (short)runtimeMin;
            }
            else if (movie.DurationMin < 1)
            {
                movie.DurationMin = 90;
            }
        }

        var releaseDateStr = GetTrimmedString(root, "release_date");
        if (!movie.HasManualOverride("release_year") &&
            !string.IsNullOrWhiteSpace(releaseDateStr) &&
            releaseDateStr.Length >= 4 &&
            short.TryParse(releaseDateStr.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) &&
            year is >= 1888 and <= 2100)
        {
            movie.ReleaseYear = year;
        }

        var origLang = GetTrimmedString(root, "original_language");
        if (!string.IsNullOrWhiteSpace(origLang))
        {
            movie.OriginalLanguage = TruncateLength(origLang, 16);
        }

        if (root.TryGetProperty("origin_country", out var countriesEl) && countriesEl.ValueKind == JsonValueKind.Array)
        {
            var countries = new List<string>();
            foreach (var c in countriesEl.EnumerateArray())
            {
                var code = c.GetString();
                if (!string.IsNullOrWhiteSpace(code))
                {
                    countries.Add(code.Trim());
                }
            }
            if (countries.Count > 0)
            {
                movie.OriginCountry = TruncateLength(string.Join(", ", countries), 100);
            }
        }

        if (!movie.HasManualOverride("genre_text") &&
            root.TryGetProperty("genres", out var genresEl) &&
            genresEl.ValueKind == JsonValueKind.Array)
        {
            var genres = new List<string>();
            foreach (var g in genresEl.EnumerateArray())
            {
                var name = GetTrimmedString(g, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    genres.Add(name);
                }
            }
            if (genres.Count > 0)
            {
                movie.GenreText = TruncateLength(string.Join(", ", genres), 160);
            }
        }

        if (!movie.HasManualOverride("synopsis") && !string.IsNullOrWhiteSpace(resolvedOverview))
        {
            movie.Synopsis = resolvedOverview.Trim();
        }

        // Strict legal rule: NEVER auto-convert US/foreign certifications (PG-13, R, etc.) to Vietnamese AgeLabel.
        // AgeLabel remains untouched (null on new TMDB imports) until verified by a cinema manager.

        var imageBase = _options.TmdbImageBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? _options.TmdbImageBaseUrl
            : _options.TmdbImageBaseUrl + "/";

        var posterPath = NormalizeImagePath(GetTrimmedString(root, "poster_path"));
        var backdropPath = NormalizeImagePath(GetTrimmedString(root, "backdrop_path"));

        bool updatedArtwork = false;
        if (!movie.HasManualOverride("poster_url") && !string.IsNullOrWhiteSpace(posterPath))
        {
            movie.PosterUrl = $"{imageBase}w780{posterPath}";
            movie.PosterW342Url = $"{imageBase}w342{posterPath}";
            updatedArtwork = true;
        }

        if (!movie.HasManualOverride("backdrop_url") && !string.IsNullOrWhiteSpace(backdropPath))
        {
            movie.BackdropUrl = $"{imageBase}w1280{backdropPath}";
            movie.BackdropW780Url = $"{imageBase}w780{backdropPath}";
            updatedArtwork = true;
        }

        if (updatedArtwork)
        {
            movie.ArtworkSource = "TMDB";
            movie.ArtworkLicenseText = "TMDB API Non-Commercial Attribution";
            movie.ArtworkAttributionText = "This product uses the TMDB API but is not endorsed or certified by TMDB.";
        }
        else if (string.IsNullOrWhiteSpace(movie.PosterUrl) && string.IsNullOrWhiteSpace(movie.BackdropUrl))
        {
            movie.ArtworkSource ??= "NONE";
        }

        if (root.TryGetProperty("credits", out var creditsEl) && creditsEl.ValueKind == JsonValueKind.Object)
        {
            if (!movie.HasManualOverride("director_names") &&
                creditsEl.TryGetProperty("crew", out var crewEl) &&
                crewEl.ValueKind == JsonValueKind.Array)
            {
                var directors = new List<string>();
                foreach (var member in crewEl.EnumerateArray())
                {
                    if (string.Equals(GetTrimmedString(member, "job"), "Director", StringComparison.OrdinalIgnoreCase))
                    {
                        var dirName = GetTrimmedString(member, "name");
                        if (!string.IsNullOrWhiteSpace(dirName) && !directors.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                        {
                            directors.Add(dirName);
                        }
                    }
                }
                if (directors.Count > 0)
                {
                    movie.DirectorNames = TruncateLength(string.Join(", ", directors), 300);
                }
            }

            if (!movie.HasManualOverride("cast_json") &&
                creditsEl.TryGetProperty("cast", out var castEl) &&
                castEl.ValueKind == JsonValueKind.Array)
            {
                var castItems = new List<MovieCreditSeedItem>();
                foreach (var actor in castEl.EnumerateArray())
                {
                    var actorName = GetTrimmedString(actor, "name");
                    if (string.IsNullOrWhiteSpace(actorName))
                    {
                        continue;
                    }
                    castItems.Add(new MovieCreditSeedItem(actorName, GetTrimmedString(actor, "character")));
                    if (castItems.Count >= 8)
                    {
                        break;
                    }
                }
                if (castItems.Count > 0)
                {
                    movie.CastJson = JsonSerializer.Serialize(castItems, CompactJsonOptions);
                }
            }
        }

        if (!movie.HasManualOverride("official_trailer_youtube_id") &&
            root.TryGetProperty("videos", out var videosEl) &&
            videosEl.ValueKind == JsonValueKind.Object &&
            videosEl.TryGetProperty("results", out var resultsEl) &&
            resultsEl.ValueKind == JsonValueKind.Array)
        {
            string? bestYoutubeKey = null;
            foreach (var v in resultsEl.EnumerateArray())
            {
                var site = GetTrimmedString(v, "site");
                var type = GetTrimmedString(v, "type");
                var key = GetTrimmedString(v, "key");
                if (!string.Equals(site, "YouTube", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                bool isTrailer = string.Equals(type, "Trailer", StringComparison.OrdinalIgnoreCase);
                bool isOfficial = v.TryGetProperty("official", out var offEl) && offEl.ValueKind == JsonValueKind.True;
                if (isTrailer && isOfficial)
                {
                    bestYoutubeKey = key;
                    break;
                }
                if (isTrailer && bestYoutubeKey is null)
                {
                    bestYoutubeKey = key;
                }
            }

            if (!string.IsNullOrWhiteSpace(bestYoutubeKey))
            {
                movie.OfficialTrailerYoutubeId = TruncateLength(bestYoutubeKey, 32);
            }
        }

        if (!movie.HasManualOverride("rating_value") &&
            !string.Equals(movie.RatingSource, "IMDB", StringComparison.OrdinalIgnoreCase) &&
            root.TryGetProperty("vote_average", out var voteAvgEl) &&
            voteAvgEl.ValueKind == JsonValueKind.Number &&
            voteAvgEl.TryGetDecimal(out var voteAvg) &&
            root.TryGetProperty("vote_count", out var voteCntEl) &&
            voteCntEl.ValueKind == JsonValueKind.Number &&
            voteCntEl.TryGetInt32(out var voteCnt) &&
            voteCnt > 0 &&
            voteAvg is >= 0m and <= 10m)
        {
            movie.RatingSource = "TMDB";
            movie.RatingValue = Math.Round(voteAvg, 1, MidpointRounding.AwayFromZero);
            movie.RatingScaleMax = 10.0m;
            movie.RatingVoteCount = voteCnt;
            movie.RatingFetchedUtc = syncedUtc;
        }

        movie.MetadataSource = "TMDB";
        movie.ExternalSyncedUtc = syncedUtc;
        movie.ExternalExpiresUtc = syncedUtc.AddDays(180);
    }

    private static int ApplyVerifiedSeedToEntity(Movie movie, VerifiedMovieSeedEntry seed, DateTime nowUtc)
    {
        int preserved = 0;
        var syncedUtc = TruncateToSeconds(nowUtc);

        movie.WikidataQid = seed.WikidataQid;
        movie.ImdbId = seed.ImdbId;
        movie.TmdbId = seed.TmdbId;

        if (!movie.HasManualOverride("title")) movie.Title = seed.Title; else preserved++;
        if (!movie.HasManualOverride("original_title")) movie.OriginalTitle = seed.OriginalTitle; else preserved++;
        if (!movie.HasManualOverride("release_year")) movie.ReleaseYear = seed.ReleaseYear; else preserved++;
        if (!movie.HasManualOverride("release_date_vn")) movie.ReleaseDateVn = seed.ReleaseDateVn; else preserved++;
        if (!movie.HasManualOverride("duration_min")) movie.DurationMin = seed.DurationMin; else preserved++;
        if (!movie.HasManualOverride("origin_country")) movie.OriginCountry = seed.OriginCountry; else preserved++;
        if (!movie.HasManualOverride("original_language")) movie.OriginalLanguage = seed.OriginalLanguage; else preserved++;
        if (!movie.HasManualOverride("genre_text")) movie.GenreText = seed.GenreText; else preserved++;
        if (!movie.HasManualOverride("director_names")) movie.DirectorNames = seed.DirectorNames; else preserved++;

        if (!movie.HasManualOverride("cast_json"))
        {
            movie.CastJson = seed.Cast.Count > 0
                ? JsonSerializer.Serialize(seed.Cast, CompactJsonOptions)
                : null;
        }
        else
        {
            preserved++;
        }

        if (!movie.HasManualOverride("synopsis")) movie.Synopsis = seed.Synopsis; else preserved++;

        if (!movie.HasManualOverride("poster_url"))
        {
            movie.PosterUrl = seed.PosterUrl;
            movie.PosterW342Url = seed.PosterW342Url;
        }
        else
        {
            preserved++;
        }

        if (!movie.HasManualOverride("backdrop_url"))
        {
            movie.BackdropUrl = seed.BackdropUrl;
            movie.BackdropW780Url = seed.BackdropW780Url;
            movie.TitleLogoUrl = seed.TitleLogoUrl;
        }
        else
        {
            preserved++;
        }

        if (!movie.HasManualOverride("poster_url") && !movie.HasManualOverride("backdrop_url"))
        {
            movie.ArtworkSource = seed.ArtworkSource;
            movie.ArtworkLicenseText = seed.ArtworkLicenseText;
            movie.ArtworkAttributionText = seed.ArtworkAttributionText;
        }

        if (!movie.HasManualOverride("official_trailer_youtube_id"))
        {
            movie.OfficialTrailerYoutubeId = seed.OfficialTrailerYoutubeId;
        }
        else
        {
            preserved++;
        }

        if (!movie.HasManualOverride("rating_value"))
        {
            if (seed.RatingValue.HasValue && seed.RatingVoteCount is > 0)
            {
                movie.RatingSource = "IMDB";
                movie.RatingValue = seed.RatingValue.Value;
                movie.RatingScaleMax = 10.0m;
                movie.RatingVoteCount = seed.RatingVoteCount.Value;
                movie.RatingFetchedUtc = VerifiedDatasetFetchedUtc;
            }
            else
            {
                movie.RatingSource = null;
                movie.RatingValue = null;
                movie.RatingScaleMax = null;
                movie.RatingVoteCount = null;
                movie.RatingFetchedUtc = null;
            }
        }
        else
        {
            preserved++;
        }

        if (!movie.HasManualOverride("age_label"))
        {
            movie.AgeLabel = seed.AgeLabelVn;
            movie.AgeLabelMarket = seed.AgeLabelVn is not null ? "VN" : null;
            movie.AgeLabelSource = seed.AgeLabelSource;
        }
        else
        {
            preserved++;
        }

        movie.MetadataSource = "WIKIDATA_IMDB_VERIFIED";
        movie.ExternalSyncedUtc = syncedUtc;
        movie.ExternalExpiresUtc = null;

        return preserved;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static DateTime TruncateToSeconds(DateTime utc)
    {
        var normalized = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return new DateTime(normalized.Year, normalized.Month, normalized.Day, normalized.Hour, normalized.Minute, normalized.Second, DateTimeKind.Utc);
    }

    private static string? GetTrimmedString(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var el) || el.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        var raw = el.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static string? NormalizeImdbId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var trimmed = raw.Trim();
        return trimmed.StartsWith("tt", StringComparison.OrdinalIgnoreCase) && trimmed.Length <= 16
            ? trimmed.ToLowerInvariant()
            : null;
    }

    private static string? NormalizeImagePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var trimmed = raw.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
    }

    private static string TruncateLength(string value, int maxLen) =>
        value.Length <= maxLen ? value : value.Substring(0, maxLen);

    private static IReadOnlyList<VerifiedMovieSeedEntry> BuildVerifiedCatalog() =>
    [
        new VerifiedMovieSeedEntry(
            Key: "charade-1963",
            WikidataQid: "Q496266",
            ImdbId: "tt0056923",
            TmdbId: 4808,
            Title: "Mê trong mê",
            OriginalTitle: "Charade",
            ReleaseYear: 1963,
            ReleaseDateVn: null,
            DurationMin: 113,
            OriginCountry: "Hoa Kỳ",
            OriginalLanguage: "en",
            GenreText: "Bí ẩn, Giật gân, Lãng mạn, Hài",
            DirectorNames: "Stanley Donen",
            Cast:
            [
                new MovieCreditSeedItem("Cary Grant", "Peter Joshua"),
                new MovieCreditSeedItem("Audrey Hepburn", "Regina Lampert"),
                new MovieCreditSeedItem("Walter Matthau", "Hamilton Bartholomew"),
                new MovieCreditSeedItem("James Coburn", "Tex Panthollow"),
                new MovieCreditSeedItem("George Kennedy", "Herman Scobie")
            ],
            Synopsis: "Trở về Paris sau kỳ nghỉ tại dãy Alps với ý định ly hôn, Regina Lampert bàng hoàng phát hiện căn hộ bị vét sạch và người chồng Charles đã bị sát hại trên chuyến tàu rời thành phố. Cô lập tức bị cuốn vào cuộc truy lùng khoản tiền 250.000 đô la Mỹ mất tích cùng ba người đàn ông bí ẩn từ quá khứ Thế chiến II của chồng và một người lạ mặt quyến rũ luôn thay đổi danh tính.",
            PosterUrl: "/media/movies/verified/charade-1963-poster.jpg",
            PosterW342Url: "/media/movies/verified/charade-1963-poster-w342.jpg",
            BackdropUrl: "/media/movies/verified/charade-1963-backdrop.jpg",
            BackdropW780Url: "/media/movies/verified/charade-1963-backdrop-w780.jpg",
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: "BJ_njww9SOQ",
            ArtworkSource: "WIKIMEDIA_COMMONS",
            ArtworkLicenseText: "Public Domain",
            ArtworkAttributionText: "Universal Pictures (1963 theatrical release, US Public Domain via Wikimedia Commons)",
            RatingValue: 7.8m,
            RatingVoteCount: 95223,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: true),

        new VerifiedMovieSeedEntry(
            Key: "charge-2022",
            WikidataQid: "Q115715270",
            ImdbId: "tt24787066",
            TmdbId: 1062079,
            Title: "Charge",
            OriginalTitle: "Charge",
            ReleaseYear: 2022,
            ReleaseDateVn: null,
            DurationMin: 4,
            OriginCountry: "Hà Lan, Iceland",
            OriginalLanguage: "zxx",
            GenreText: "Khoa học viễn tưởng, Hành động, Hoạt hình",
            DirectorNames: "Hjalti Hjálmarsson",
            Cast:
            [
                new MovieCreditSeedItem("Blender Studio Ensemble", "Nhân vật hoạt hình không thoại")
            ],
            Synopsis: "Giữa vùng đất băng giá hoang tàn của tương lai hậu công nghiệp, một ông lão nghèo khó mang chân tay giả cơ khí đột nhập vào nhà máy năng lượng tự động để tìm kiếm một viên pin còn hoạt động, trước khi đối mặt với cỗ máy bảo vệ an ninh không khoan nhượng.",
            PosterUrl: "/media/movies/verified/charge-2022-poster.jpg",
            PosterW342Url: "/media/movies/verified/charge-2022-poster-w342.jpg",
            BackdropUrl: "/media/movies/verified/charge-2022-backdrop.jpg",
            BackdropW780Url: "/media/movies/verified/charge-2022-backdrop-w780.jpg",
            TitleLogoUrl: "/media/movies/verified/charge-2022-logo.png",
            OfficialTrailerYoutubeId: "UXqq0ZvbOnk",
            ArtworkSource: "WIKIMEDIA_COMMONS",
            ArtworkLicenseText: "CC BY 4.0",
            ArtworkAttributionText: "© Blender Studio (studio.blender.org/films/charge) — Licensed under CC BY 4.0",
            RatingValue: 7.6m,
            RatingVoteCount: 71,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: true),

        new VerifiedMovieSeedEntry(
            Key: "tears-of-steel-2012",
            WikidataQid: "Q1173161",
            ImdbId: "tt2285752",
            TmdbId: 133701,
            Title: "Tears of Steel",
            OriginalTitle: "Tears of Steel",
            ReleaseYear: 2012,
            ReleaseDateVn: null,
            DurationMin: 12,
            OriginCountry: "Hà Lan",
            OriginalLanguage: "en",
            GenreText: "Khoa học viễn tưởng, Hành động",
            DirectorNames: "Ian Hubert",
            Cast:
            [
                new MovieCreditSeedItem("Derek de Lint", "Old Thom"),
                new MovieCreditSeedItem("Vanja Rukavina", "Thom"),
                new MovieCreditSeedItem("Denise Rebergen", "Celia"),
                new MovieCreditSeedItem("Sergio Hasselbaink", "Barley")
            ],
            Synopsis: "Trong bối cảnh Amsterdam đổ nát trước thảm họa robot nổi dậy, một nhóm chiến binh và nhà khoa học tập hợp tại nhà thờ cổ Oude Kerk để tái hiện ký ức quyết định giữa chàng trai Thom và nhà chế tạo Celia nhằm ngăn chặn sự hủy diệt thế giới.",
            PosterUrl: "/media/movies/verified/tears-of-steel-2012-poster.png",
            PosterW342Url: "/media/movies/verified/tears-of-steel-2012-poster-w342.jpg",
            BackdropUrl: "/media/movies/verified/tears-of-steel-2012-backdrop.jpg",
            BackdropW780Url: "/media/movies/verified/tears-of-steel-2012-backdrop-w780.jpg",
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: "R6MlUcmOul8",
            ArtworkSource: "WIKIMEDIA_COMMONS",
            ArtworkLicenseText: "CC BY 3.0",
            ArtworkAttributionText: "© Blender Foundation | mango.blender.org — Licensed under CC BY 3.0",
            RatingValue: 5.5m,
            RatingVoteCount: 1576,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: true),

        new VerifiedMovieSeedEntry(
            Key: "sintel-2010",
            WikidataQid: "Q925587",
            ImdbId: "tt1727587",
            TmdbId: 45745,
            Title: "Sintel",
            OriginalTitle: "Sintel",
            ReleaseYear: 2010,
            ReleaseDateVn: null,
            DurationMin: 15,
            OriginCountry: "Hà Lan",
            OriginalLanguage: "en",
            GenreText: "Kỳ ảo, Phiêu lưu, Hoạt hình",
            DirectorNames: "Colin Levy",
            Cast:
            [
                new MovieCreditSeedItem("Halina Reijn", "Sintel"),
                new MovieCreditSeedItem("Thom Hoffman", "Shaman")
            ],
            Synopsis: "Cô gái trẻ đơn độc Sintel băng qua những vùng núi tuyết và sa mạc khắc nghiệt để tìm lại Scales — chú rồng nhỏ bị thương từng được cô cứu sống và kết bạn trước khi bị một con rồng trưởng thành bắt đi, dẫn đến một sự thật bi kịch ở cuối hành trình.",
            PosterUrl: "/media/movies/verified/sintel-2010-poster.jpg",
            PosterW342Url: "/media/movies/verified/sintel-2010-poster-w342.jpg",
            BackdropUrl: "/media/movies/verified/sintel-2010-backdrop.jpg",
            BackdropW780Url: "/media/movies/verified/sintel-2010-backdrop-w780.jpg",
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: "eRsGyueVLvQ",
            ArtworkSource: "WIKIMEDIA_COMMONS",
            ArtworkLicenseText: "CC BY 3.0",
            ArtworkAttributionText: "© Blender Foundation | durian.blender.org — Licensed under CC BY 3.0",
            RatingValue: 7.4m,
            RatingVoteCount: 3802,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: true),

        new VerifiedMovieSeedEntry(
            Key: "spring-2019",
            WikidataQid: "Q62770443",
            ImdbId: "tt9249278",
            TmdbId: 593048,
            Title: "Spring",
            OriginalTitle: "Spring",
            ReleaseYear: 2019,
            ReleaseDateVn: null,
            DurationMin: 8,
            OriginCountry: "Đức, Hà Lan",
            OriginalLanguage: "zxx",
            GenreText: "Kỳ ảo, Gia đình, Hoạt hình",
            DirectorNames: "Andy Goralczyk",
            Cast:
            [
                new MovieCreditSeedItem("Blender Animation Studio", "Spring & chú chó chăn cừu")
            ],
            Synopsis: "Câu chuyện giàu chất thơ về cô bé chăn cừu Spring và chú chó nhỏ dũng cảm đối diện với những linh hồn cổ xưa trên đỉnh núi mây phủ để đánh thức vòng tuần hoàn mùa xuân cho thung lũng băng giá bên dưới.",
            PosterUrl: "/media/movies/verified/spring-2019-poster.jpg",
            PosterW342Url: "/media/movies/verified/spring-2019-poster-w342.jpg",
            BackdropUrl: "/media/movies/verified/spring-2019-backdrop.png",
            BackdropW780Url: "/media/movies/verified/spring-2019-backdrop-w780.jpg",
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: "WhWc3b3KhnY",
            ArtworkSource: "WIKIMEDIA_COMMONS",
            ArtworkLicenseText: "CC BY 4.0 / CC0",
            ArtworkAttributionText: "© Blender Foundation / Andy Goralczyk — Poster licensed under CC BY 4.0, Backdrop CC0",
            RatingValue: 7.2m,
            RatingVoteCount: 292,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: true),

        new VerifiedMovieSeedEntry(
            Key: "cosmos-2015",
            WikidataQid: "Q20968125",
            ImdbId: "tt4957236",
            TmdbId: 358332,
            Title: "Cosmos Laundromat",
            OriginalTitle: "Cosmos Laundromat",
            ReleaseYear: 2015,
            ReleaseDateVn: null,
            DurationMin: 12,
            OriginCountry: "Hà Lan",
            OriginalLanguage: "en",
            GenreText: "Hài kịch đen, Khoa học viễn tưởng, Kỳ ảo, Hoạt hình",
            DirectorNames: "Mathieu Auvray",
            Cast:
            [
                new MovieCreditSeedItem("Pierre Bokma", "Franck"),
                new MovieCreditSeedItem("Reinout Scholten van Aschat", "Victor")
            ],
            Synopsis: "Trên một hòn đảo hoang gió lốc, chú cừu tuyệt vọng Franck bất ngờ gặp gã chào hàng lịch lãm Victor — người mời Franck bước qua chiếc máy giặt kỳ lạ để thử sống nhiều kiếp sống khác nhau trong vũ trụ.",
            PosterUrl: "/media/movies/verified/cosmos-2015-poster.jpg",
            PosterW342Url: "/media/movies/verified/cosmos-2015-poster-w342.jpg",
            BackdropUrl: "/media/movies/verified/cosmos-2015-backdrop.jpg",
            BackdropW780Url: "/media/movies/verified/cosmos-2015-backdrop-w780.jpg",
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: "Y-rmzh0PI3c",
            ArtworkSource: "WIKIMEDIA_COMMONS",
            ArtworkLicenseText: "CC BY 4.0 / CC BY 3.0",
            ArtworkAttributionText: "© Blender Foundation | gooseberry.blender.org — Poster CC BY 4.0 (Andy Goralczyk / Blender Foundation), Backdrop CC BY 3.0",
            RatingValue: 6.8m,
            RatingVoteCount: 1204,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: false),

        new VerifiedMovieSeedEntry(
            Key: "notld-1968",
            WikidataQid: "Q623051",
            ImdbId: "tt0063350",
            TmdbId: 10331,
            Title: "Night of the Living Dead",
            OriginalTitle: "Night of the Living Dead",
            ReleaseYear: 1968,
            ReleaseDateVn: null,
            DurationMin: 96,
            OriginCountry: "Hoa Kỳ",
            OriginalLanguage: "en",
            GenreText: "Kinh dị, Giật gân",
            DirectorNames: "George A. Romero",
            Cast:
            [
                new MovieCreditSeedItem("Duane Jones", "Ben"),
                new MovieCreditSeedItem("Judith O'Dea", "Barbra"),
                new MovieCreditSeedItem("Karl Hardman", "Harry Cooper")
            ],
            Synopsis: "Bảy người xa lạ cố thủ trong một trang trại hẻo lánh ở vùng nông thôn Pennsylvania suốt một đêm kinh hoàng khi những xác chết bí ẩn sống dậy bao vây bên ngoài, buộc họ vừa phải chống trả sinh tồn vừa đối mặt với mâu thuẫn căng thẳng từ bên trong.",
            PosterUrl: "/media/movies/verified/notld-1968-poster.jpg",
            PosterW342Url: "/media/movies/verified/notld-1968-poster-w342.jpg",
            BackdropUrl: "/media/movies/verified/notld-1968-backdrop.jpg",
            BackdropW780Url: "/media/movies/verified/notld-1968-backdrop-w780.jpg",
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: "jflXLvFb_wo",
            ArtworkSource: "WIKIMEDIA_COMMONS",
            ArtworkLicenseText: "Public Domain",
            ArtworkAttributionText: "Image Ten / Continental Distributing (1968 US Public Domain release via Wikimedia Commons)",
            RatingValue: 7.8m,
            RatingVoteCount: 153645,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: true),

        new VerifiedMovieSeedEntry(
            Key: "his-girl-friday-1940",
            WikidataQid: "Q1066714",
            ImdbId: "tt0032599",
            TmdbId: 3085,
            Title: "His Girl Friday",
            OriginalTitle: "His Girl Friday",
            ReleaseYear: 1940,
            ReleaseDateVn: null,
            DurationMin: 92,
            OriginCountry: "Hoa Kỳ",
            OriginalLanguage: "en",
            GenreText: "Hài kịch lãng mạn, Báo chí",
            DirectorNames: "Howard Hawks",
            Cast:
            [
                new MovieCreditSeedItem("Cary Grant", "Walter Burns"),
                new MovieCreditSeedItem("Rosalind Russell", "Hildy Johnson"),
                new MovieCreditSeedItem("Ralph Bellamy", "Bruce Baldwin")
            ],
            Synopsis: "Chủ bút báo chí sắc sảo Walter Burns tìm mọi mưu mẹo vừa hài hước vừa quyết liệt để giữ chân cây phóng viên điều tra xuất sắc nhất — đồng thời là vợ cũ của anh, Hildy Johnson — ngay trước ngày cô định lên xe hoa về sống cuộc đời bình lặng bằng cách giao cho cô vụ án nóng nhất thành phố.",
            PosterUrl: "/media/movies/verified/his-girl-friday-1940-poster.jpg",
            PosterW342Url: "/media/movies/verified/his-girl-friday-1940-poster-w342.jpg",
            BackdropUrl: "/media/movies/verified/his-girl-friday-1940-backdrop.jpg",
            BackdropW780Url: "/media/movies/verified/his-girl-friday-1940-backdrop-w780.jpg",
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: "5pVxMeditpM",
            ArtworkSource: "WIKIMEDIA_COMMONS",
            ArtworkLicenseText: "Public Domain",
            ArtworkAttributionText: "Columbia Pictures (1940 US Public Domain release via Wikimedia Commons)",
            RatingValue: 7.8m,
            RatingVoteCount: 68904,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: true),

        new VerifiedMovieSeedEntry(
            Key: "the-general-1926",
            WikidataQid: "Q850159",
            ImdbId: "tt0017925",
            TmdbId: 961,
            Title: "The General",
            OriginalTitle: "The General",
            ReleaseYear: 1926,
            ReleaseDateVn: null,
            DurationMin: 78,
            OriginCountry: "Hoa Kỳ",
            OriginalLanguage: "en",
            GenreText: "Hành động, Phiêu lưu, Hài kịch lịch sử",
            DirectorNames: "Clyde Bruckman, Buster Keaton",
            Cast:
            [
                new MovieCreditSeedItem("Buster Keaton", "Johnnie Gray"),
                new MovieCreditSeedItem("Marion Mack", "Annabelle Lee")
            ],
            Synopsis: "Khi đoàn tàu hỏa yêu quý mang tên The General cùng người thương Annabelle Lee bị gián điệp đối phương đánh cắp, kỹ sư lái tàu Johnnie Gray một mình dấn thân vào cuộc rượt đuổi bằng đầu máy hơi nước xuyên qua chiến tuyến đầy mạo hiểm và hài hước.",
            PosterUrl: "/media/movies/verified/the-general-1926-poster.png",
            PosterW342Url: "/media/movies/verified/the-general-1926-poster-w342.jpg",
            BackdropUrl: "/media/movies/verified/the-general-1926-backdrop.jpg",
            BackdropW780Url: "/media/movies/verified/the-general-1926-backdrop-w780.jpg",
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: "lbzorV_jEW8",
            ArtworkSource: "WIKIMEDIA_COMMONS",
            ArtworkLicenseText: "Public Domain",
            ArtworkAttributionText: "Buster Keaton Productions / United Artists (1926 Public Domain via Wikimedia Commons)",
            RatingValue: 8.1m,
            RatingVoteCount: 106657,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: false),

        new VerifiedMovieSeedEntry(
            Key: "mai-2024",
            WikidataQid: "Q124516759",
            ImdbId: "tt31174028",
            TmdbId: 1210973,
            Title: "Mai",
            OriginalTitle: "Mai",
            ReleaseYear: 2024,
            ReleaseDateVn: new DateOnly(2024, 2, 10),
            DurationMin: 131,
            OriginCountry: "Việt Nam",
            OriginalLanguage: "vi",
            GenreText: "Tâm lý, Tình cảm, Gia đình",
            DirectorNames: "Trấn Thành",
            Cast:
            [
                new MovieCreditSeedItem("Phương Anh Đào", "Mai"),
                new MovieCreditSeedItem("Tuấn Trần", "Dương"),
                new MovieCreditSeedItem("Hồng Đào", "Bà Đào"),
                new MovieCreditSeedItem("Trấn Thành", "Ông Hoàng"),
                new MovieCreditSeedItem("Uyển Ân", "Bình Minh")
            ],
            Synopsis: "Người phụ nữ tên Mai mang nhiều tổn thương quá khứ làm nghề massage trị liệu tại Thành phố Hồ Chí Minh chuyển đến sống ở khu chung cư cũ, nơi cô gặp Dương — chàng trai trẻ phóng khoáng sống đối diện — và đối diện với những định kiến gia đình cùng lựa chọn hạnh phúc của chính mình.",
            PosterUrl: null,
            PosterW342Url: null,
            BackdropUrl: null,
            BackdropW780Url: null,
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: null,
            ArtworkSource: "PENDING_TMDB_API_KEY",
            ArtworkLicenseText: null,
            ArtworkAttributionText: "Chờ kết nối BGP_TMDB_READ_TOKEN (TMDB #1210973) — không lưu tĩnh ảnh poster thương mại khi chưa có khóa API.",
            RatingValue: 6.9m,
            RatingVoteCount: 1926,
            AgeLabelVn: "T18",
            AgeLabelSource: "Cục Điện ảnh Việt Nam (Phát hành rạp Tết Giáp Thìn 2024)",
            HasDefaultShowcaseShowtime: false),

        new VerifiedMovieSeedEntry(
            Key: "dao-pho-va-piano-2024",
            WikidataQid: "Q124620657",
            ImdbId: "tt31438076",
            TmdbId: 1247240,
            Title: "Đào, phở và piano",
            OriginalTitle: "Đào, phở và piano",
            ReleaseYear: 2024,
            ReleaseDateVn: new DateOnly(2024, 2, 10),
            DurationMin: 100,
            OriginCountry: "Việt Nam",
            OriginalLanguage: "vi",
            GenreText: "Lịch sử, Chiến tranh, Tình cảm",
            DirectorNames: "Phi Tiến Sơn",
            Cast:
            [
                new MovieCreditSeedItem("Doãn Quốc Đam", "Dân"),
                new MovieCreditSeedItem("Cao Thị Thùy Linh", "Hương"),
                new MovieCreditSeedItem("NSND Trung Hiếu", "Cha xứ"),
                new MovieCreditSeedItem("NSƯT Trần Lực", "Ông họa sĩ")
            ],
            Synopsis: "Lấy bối cảnh những ngày cuối cùng của cuộc chiến bảo vệ Hà Nội mùa đông năm 1946 đầu năm 1947, chàng tự vệ Dân và cô tiểu thư Hà thành Hương tìm thấy nhau giữa chiến lũy phố cổ cùng cành đào Nhật Tân, bát phở nóng và tiếng đàn dương cầm.",
            PosterUrl: null,
            PosterW342Url: null,
            BackdropUrl: null,
            BackdropW780Url: null,
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: null,
            ArtworkSource: "PENDING_TMDB_API_KEY",
            ArtworkLicenseText: null,
            ArtworkAttributionText: "Chờ kết nối BGP_TMDB_READ_TOKEN (TMDB #1247240) — không lưu tĩnh ảnh poster thương mại khi chưa có khóa API.",
            RatingValue: 5.6m,
            RatingVoteCount: 248,
            AgeLabelVn: "T13",
            AgeLabelSource: "Cục Điện ảnh Việt Nam (Phát hành rạp 02/2024)",
            HasDefaultShowcaseShowtime: false)
    ];

    private static IReadOnlyList<VerifiedMovieSeedEntry> BuildImportableVerifiedCatalog() =>
    [
        new VerifiedMovieSeedEntry(
            Key: "elephants-dream-2006",
            WikidataQid: "Q737719",
            ImdbId: "tt0807840",
            TmdbId: 39079,
            Title: "Elephants Dream",
            OriginalTitle: "Elephants Dream",
            ReleaseYear: 2006,
            ReleaseDateVn: null,
            DurationMin: 11,
            OriginCountry: "Hà Lan",
            OriginalLanguage: "en",
            GenreText: "Khoa học viễn tưởng, Siêu thực, Hoạt hình",
            DirectorNames: "Bassam Kurdali",
            Cast:
            [
                new MovieCreditSeedItem("Tygo Gernandt", "Proog"),
                new MovieCreditSeedItem("Cas Jansen", "Emo")
            ],
            Synopsis: "Bộ phim hoạt hình mã nguồn mở đầu tiên trên thế giới (Project Orange) do Blender Foundation sản xuất, theo chân người dẫn đường kỳ quặc Proog và chàng trai trẻ hoài nghi Emo khám phá mê cung cơ khí khổng lồ luôn biến đổi không ngừng.",
            PosterUrl: null,
            PosterW342Url: null,
            BackdropUrl: null,
            BackdropW780Url: null,
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: null,
            ArtworkSource: "NONE",
            ArtworkLicenseText: "CC BY 2.5",
            ArtworkAttributionText: "© 2006 Blender Foundation / Netherlands Media Art Institute | orange.blender.org — Licensed under Creative Commons Attribution 2.5",
            RatingValue: null,
            RatingVoteCount: null,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: false),

        new VerifiedMovieSeedEntry(
            Key: "big-buck-bunny-2008",
            WikidataQid: "Q731114",
            ImdbId: "tt1254207",
            TmdbId: 10378,
            Title: "Big Buck Bunny",
            OriginalTitle: "Big Buck Bunny",
            ReleaseYear: 2008,
            ReleaseDateVn: null,
            DurationMin: 10,
            OriginCountry: "Hà Lan",
            OriginalLanguage: "zxx",
            GenreText: "Hài, Gia đình, Hoạt hình",
            DirectorNames: "Sacha Goedegebure",
            Cast:
            [
                new MovieCreditSeedItem("Blender Institute Ensemble", "Nhân vật hoạt hình không thoại")
            ],
            Synopsis: "Dự án phim mở thứ hai của Blender Institute (Project Peach), kể câu chuyện hài hước về chú thỏ khổng lồ hiền lành quyết định lên kế hoạch đáp trả tinh quái bộ ba sóc bay chuyên bắt nạt các sinh vật nhỏ trong khu rừng.",
            PosterUrl: null,
            PosterW342Url: null,
            BackdropUrl: null,
            BackdropW780Url: null,
            TitleLogoUrl: null,
            OfficialTrailerYoutubeId: null,
            ArtworkSource: "NONE",
            ArtworkLicenseText: "CC BY 3.0",
            ArtworkAttributionText: "© 2008 Blender Foundation | peach.blender.org — Licensed under Creative Commons Attribution 3.0",
            RatingValue: null,
            RatingVoteCount: null,
            AgeLabelVn: null,
            AgeLabelSource: null,
            HasDefaultShowcaseShowtime: false)
    ];
}
