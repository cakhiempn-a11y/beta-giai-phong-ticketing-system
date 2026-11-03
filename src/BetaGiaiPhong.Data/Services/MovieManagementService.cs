using System.Data;
using System.Text.RegularExpressions;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Services;

public sealed record MovieProvenanceEditInput(
    string? OriginalTitle = null,
    short? ReleaseYear = null,
    string? DirectorNames = null,
    string? OriginCountry = null,
    string? OriginalLanguage = null,
    string? WikidataQid = null,
    string? ImdbId = null,
    int? TmdbId = null,
    string? BackdropUrl = null,
    string? OfficialTrailerYoutubeId = null,
    bool ApplyExplicitOverrideLocks = false,
    IReadOnlyCollection<string>? ExplicitOverrideLocks = null);

public sealed class MovieManagementService(BetaDbContext db, ISystemClock clock)
{
    private static readonly HashSet<string> AllowedAgeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "P", "K", "T13", "T16", "T18", "C"
    };

    private static readonly Regex WikidataQidRegex = new(@"^Q\d{1,12}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ImdbIdRegex = new(@"^tt\d{5,12}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YoutubeIdRegex = new(@"^[A-Za-z0-9_-]{6,20}$", RegexOptions.Compiled);

    public async Task<long> CreateMovieAsync(
        long managerId,
        string title,
        short durationMin,
        string? genreText,
        string? ageLabel,
        string? synopsis,
        string? posterUrl,
        CancellationToken ct = default,
        MovieProvenanceEditInput? provenance = null)
    {
        ValidateMovieInputs(title, durationMin, genreText, ageLabel, posterUrl);
        ValidateProvenanceInputs(provenance);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await SecurityRules.EnsureRoleAsync(db, managerId, ct, "MANAGER", "ADMIN");

        var now = clock.UtcNow;
        var nextAge = string.IsNullOrWhiteSpace(ageLabel) ? null : ageLabel.Trim().ToUpperInvariant();
        var nextPoster = string.IsNullOrWhiteSpace(posterUrl) ? null : posterUrl.Trim();
        var nextBackdrop = string.IsNullOrWhiteSpace(provenance?.BackdropUrl) ? null : provenance!.BackdropUrl!.Trim();
        var qid = string.IsNullOrWhiteSpace(provenance?.WikidataQid) ? null : provenance!.WikidataQid!.Trim().ToUpperInvariant();
        var imdb = string.IsNullOrWhiteSpace(provenance?.ImdbId) ? null : provenance!.ImdbId!.Trim().ToLowerInvariant();
        var tmdb = provenance?.TmdbId is > 0 ? provenance.TmdbId : null;

        var movie = new Movie
        {
            Title = title.Trim(),
            OriginalTitle = string.IsNullOrWhiteSpace(provenance?.OriginalTitle) ? null : provenance!.OriginalTitle!.Trim(),
            ReleaseYear = provenance?.ReleaseYear,
            DirectorNames = string.IsNullOrWhiteSpace(provenance?.DirectorNames) ? null : provenance!.DirectorNames!.Trim(),
            OriginCountry = string.IsNullOrWhiteSpace(provenance?.OriginCountry) ? null : provenance!.OriginCountry!.Trim(),
            OriginalLanguage = string.IsNullOrWhiteSpace(provenance?.OriginalLanguage) ? null : provenance!.OriginalLanguage!.Trim(),
            WikidataQid = qid,
            ImdbId = imdb,
            TmdbId = tmdb,
            DurationMin = durationMin,
            GenreText = string.IsNullOrWhiteSpace(genreText) ? null : genreText.Trim(),
            AgeLabel = nextAge,
            AgeLabelMarket = nextAge != null ? "VN" : null,
            AgeLabelSource = nextAge != null ? "MANUAL_MANAGER" : null,
            Synopsis = string.IsNullOrWhiteSpace(synopsis) ? null : synopsis.Trim(),
            PosterUrl = nextPoster,
            BackdropUrl = nextBackdrop,
            OfficialTrailerYoutubeId = string.IsNullOrWhiteSpace(provenance?.OfficialTrailerYoutubeId)
                ? null
                : provenance!.OfficialTrailerYoutubeId!.Trim(),
            ArtworkSource = nextPoster != null || nextBackdrop != null ? "DISTRIBUTOR" : "NONE",
            MetadataSource = qid != null || imdb != null || tmdb != null ? "VERIFIED_HYBRID_MANAGER" : "MANUAL_MANAGER",
            Status = "DRAFT"
        };

        if (provenance?.ExplicitOverrideLocks is { Count: > 0 })
        {
            movie.SetManualOverrides(provenance.ExplicitOverrideLocks);
        }

        db.Movies.Add(movie);
        await db.SaveChangesAsync(ct);

        db.AuditLogs.Add(Auditing.Event(managerId, "MOVIE_CREATED", "Movie",
            movie.MovieId.ToString(), new { movie.Title, movie.DurationMin, movie.Status, movie.WikidataQid, movie.ImdbId, movie.TmdbId }, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return movie.MovieId;
    }

    public async Task UpdateMovieAsync(
        long managerId,
        long movieId,
        string title,
        short durationMin,
        string? genreText,
        string? ageLabel,
        string? synopsis,
        string? posterUrl,
        CancellationToken ct = default,
        MovieProvenanceEditInput? provenance = null)
    {
        ValidateMovieInputs(title, durationMin, genreText, ageLabel, posterUrl);
        ValidateProvenanceInputs(provenance);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await SqlServerLocks.MovieAsync(db, movieId, ct))
            throw new BusinessRuleException("MOVIE_NOT_FOUND", "Không tìm thấy phim yêu cầu.");
        await SecurityRules.EnsureRoleAsync(db, managerId, ct, "MANAGER", "ADMIN");

        var movie = await db.Movies.SingleOrDefaultAsync(m => m.MovieId == movieId, ct)
            ?? throw new BusinessRuleException("MOVIE_NOT_FOUND", "Không tìm thấy phim yêu cầu.");

        if (movie.Status == "ARCHIVED")
        {
            throw new BusinessRuleException("INVALID_STATUS",
                "Không thể chỉnh sửa phim đã lưu trữ.");
        }

        // Guard: Cannot alter duration if movie has ongoing or upcoming uncancelled showtimes
        if (movie.DurationMin != durationMin)
        {
            var nowUtc = clock.UtcNow;
            var hasActiveShowtimes = await db.Showtimes
                .AnyAsync(s => s.MovieId == movieId && s.Status != "CANCELLED" && s.EndUtc > nowUtc, ct);

            if (hasActiveShowtimes)
            {
                throw new BusinessRuleException("CANNOT_CHANGE_DURATION",
                    "Không thể thay đổi thời lượng của phim khi đang có suất chiếu chưa kết thúc.");
            }
        }

        var nextTitle = title.Trim();
        var nextGenre = string.IsNullOrWhiteSpace(genreText) ? null : genreText.Trim();
        var nextAge = string.IsNullOrWhiteSpace(ageLabel) ? null : ageLabel.Trim().ToUpperInvariant();
        var nextSynopsis = string.IsNullOrWhiteSpace(synopsis) ? null : synopsis.Trim();
        var nextPoster = string.IsNullOrWhiteSpace(posterUrl) ? null : posterUrl.Trim();

        var overridden = new List<string>();
        if (!string.Equals(movie.Title, nextTitle, StringComparison.Ordinal)) overridden.Add("title");
        if (movie.DurationMin != durationMin) overridden.Add("duration_min");
        if (!string.Equals(movie.GenreText, nextGenre, StringComparison.Ordinal)) overridden.Add("genre_text");
        if (!string.Equals(movie.AgeLabel, nextAge, StringComparison.OrdinalIgnoreCase)) overridden.Add("age_label");
        if (!string.Equals(movie.Synopsis, nextSynopsis, StringComparison.Ordinal)) overridden.Add("synopsis");
        if (!string.Equals(movie.PosterUrl, nextPoster, StringComparison.Ordinal)) overridden.Add("poster_url");

        movie.Title = nextTitle;
        movie.DurationMin = durationMin;
        movie.GenreText = nextGenre;
        movie.AgeLabel = nextAge;
        if (nextAge != null)
        {
            movie.AgeLabelMarket = "VN";
            movie.AgeLabelSource ??= "MANUAL_MANAGER";
        }
        else
        {
            movie.AgeLabelMarket = null;
            movie.AgeLabelSource = null;
        }
        movie.Synopsis = nextSynopsis;
        movie.PosterUrl = nextPoster;

        if (provenance is not null)
        {
            var nextOrigTitle = string.IsNullOrWhiteSpace(provenance.OriginalTitle) ? null : provenance.OriginalTitle.Trim();
            var nextDirectors = string.IsNullOrWhiteSpace(provenance.DirectorNames) ? null : provenance.DirectorNames.Trim();
            var nextCountry = string.IsNullOrWhiteSpace(provenance.OriginCountry) ? null : provenance.OriginCountry.Trim();
            var nextLang = string.IsNullOrWhiteSpace(provenance.OriginalLanguage) ? null : provenance.OriginalLanguage.Trim();
            var nextBackdrop = string.IsNullOrWhiteSpace(provenance.BackdropUrl) ? null : provenance.BackdropUrl.Trim();
            var nextTrailer = string.IsNullOrWhiteSpace(provenance.OfficialTrailerYoutubeId) ? null : provenance.OfficialTrailerYoutubeId.Trim();
            var nextQid = string.IsNullOrWhiteSpace(provenance.WikidataQid) ? null : provenance.WikidataQid.Trim().ToUpperInvariant();
            var nextImdb = string.IsNullOrWhiteSpace(provenance.ImdbId) ? null : provenance.ImdbId.Trim().ToLowerInvariant();
            var nextTmdb = provenance.TmdbId is > 0 ? provenance.TmdbId : null;

            if (!string.Equals(movie.OriginalTitle, nextOrigTitle, StringComparison.Ordinal)) overridden.Add("original_title");
            if (movie.ReleaseYear != provenance.ReleaseYear) overridden.Add("release_year");
            if (!string.Equals(movie.DirectorNames, nextDirectors, StringComparison.Ordinal)) overridden.Add("director_names");
            if (!string.Equals(movie.BackdropUrl, nextBackdrop, StringComparison.Ordinal)) overridden.Add("backdrop_url");
            if (!string.Equals(movie.OfficialTrailerYoutubeId, nextTrailer, StringComparison.Ordinal)) overridden.Add("official_trailer_youtube_id");

            movie.OriginalTitle = nextOrigTitle;
            movie.ReleaseYear = provenance.ReleaseYear;
            movie.DirectorNames = nextDirectors;
            movie.OriginCountry = nextCountry;
            movie.OriginalLanguage = nextLang;
            movie.BackdropUrl = nextBackdrop;
            movie.OfficialTrailerYoutubeId = nextTrailer;
            movie.WikidataQid = nextQid;
            movie.ImdbId = nextImdb;
            movie.TmdbId = nextTmdb;

            if (provenance.ApplyExplicitOverrideLocks)
            {
                movie.SetManualOverrides(provenance.ExplicitOverrideLocks);
            }
        }

        if (overridden.Count > 0)
        {
            movie.AddManualOverrides(overridden.ToArray());
        }

        if ((movie.PosterUrl != null || movie.BackdropUrl != null) && (movie.ArtworkSource == null || movie.ArtworkSource == "NONE"))
        {
            movie.ArtworkSource = "DISTRIBUTOR";
        }
        else if (movie.PosterUrl == null && movie.BackdropUrl == null && movie.ArtworkSource == "DISTRIBUTOR")
        {
            movie.ArtworkSource = "NONE";
        }

        var now = clock.UtcNow;
        db.AuditLogs.Add(Auditing.Event(managerId, "MOVIE_UPDATED", "Movie",
            movie.MovieId.ToString(), new { movie.Title, movie.DurationMin, movie.Status, movie.ManualOverrideMask }, now));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task PublishMovieAsync(long managerId, long movieId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await SqlServerLocks.MovieAsync(db, movieId, ct))
            throw new BusinessRuleException("MOVIE_NOT_FOUND", "Không tìm thấy phim yêu cầu.");
        await SecurityRules.EnsureRoleAsync(db, managerId, ct, "MANAGER", "ADMIN");

        var movie = await db.Movies.SingleOrDefaultAsync(m => m.MovieId == movieId, ct)
            ?? throw new BusinessRuleException("MOVIE_NOT_FOUND", "Không tìm thấy phim yêu cầu.");

        if (movie.Status != "DRAFT")
        {
            throw new BusinessRuleException("INVALID_STATUS",
                $"Chỉ có thể xuất bản phim đang ở trạng thái DRAFT. Trạng thái hiện tại: {movie.Status}.");
        }

        movie.Status = "PUBLISHED";
        var now = clock.UtcNow;
        db.AuditLogs.Add(Auditing.Event(managerId, "MOVIE_PUBLISHED", "Movie",
            movie.MovieId.ToString(), new { movie.Title }, now));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ArchiveMovieAsync(long managerId, long movieId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await SqlServerLocks.MovieAsync(db, movieId, ct))
            throw new BusinessRuleException("MOVIE_NOT_FOUND", "Không tìm thấy phim yêu cầu.");
        await SecurityRules.EnsureRoleAsync(db, managerId, ct, "MANAGER", "ADMIN");

        var movie = await db.Movies.SingleOrDefaultAsync(m => m.MovieId == movieId, ct)
            ?? throw new BusinessRuleException("MOVIE_NOT_FOUND", "Không tìm thấy phim yêu cầu.");

        if (movie.Status == "ARCHIVED")
        {
            return;
        }

        var now = clock.UtcNow;

        // Strict guard: Do not archive if movie still has active or upcoming uncancelled showtimes
        var hasActiveShowtimes = await db.Showtimes
            .AnyAsync(s => s.MovieId == movieId && s.Status != "CANCELLED" && s.EndUtc > now, ct);

        if (hasActiveShowtimes)
        {
            throw new BusinessRuleException("CANNOT_ARCHIVE_ACTIVE_SHOWTIMES",
                "Không thể lưu trữ phim khi vẫn còn suất chiếu chưa kết thúc hoặc chưa hủy.");
        }

        movie.Status = "ARCHIVED";

        db.AuditLogs.Add(Auditing.Event(managerId, "MOVIE_ARCHIVED", "Movie",
            movie.MovieId.ToString(), new { movie.Title }, now));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static void ValidateMovieInputs(
        string title, short durationMin, string? genreText, string? ageLabel, string? posterUrl)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new BusinessRuleException("INVALID_TITLE", "Tên phim không được để trống.");

        if (title.Trim().Length > 250)
            throw new BusinessRuleException("INVALID_TITLE", "Tên phim không được vượt quá 250 ký tự.");

        if (durationMin is < 1 or > 480)
            throw new BusinessRuleException("INVALID_DURATION", "Thời lượng phim phải từ 1 đến 480 phút.");

        if (genreText != null && genreText.Trim().Length > 160)
            throw new BusinessRuleException("INVALID_GENRE", "Thể loại phim không được vượt quá 160 ký tự.");

        if (!string.IsNullOrWhiteSpace(ageLabel))
        {
            var trimmedAge = ageLabel.Trim().ToUpperInvariant();
            if (trimmedAge.Length > 24 || !AllowedAgeLabels.Contains(trimmedAge))
            {
                throw new BusinessRuleException("INVALID_AGE_LABEL",
                    $"Nhãn độ tuổi không hợp lệ. Các giá trị được phép: {string.Join(", ", AllowedAgeLabels)}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(posterUrl))
        {
            var trimmed = posterUrl.Trim();
            if (trimmed.Length > 500)
                throw new BusinessRuleException("INVALID_POSTER_URL", "Đường dẫn poster không được vượt quá 500 ký tự.");

            // Reject dangerous patterns: javascript:, data:, protocol-relative //, backslash tricks
            if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("\\", StringComparison.Ordinal) ||
                trimmed.Contains('\\') ||
                trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException("INVALID_POSTER_URL",
                    "Đường dẫn poster không an toàn hoặc chứa ký tự không hợp lệ.");
            }

            if (trimmed.StartsWith("/", StringComparison.Ordinal))
                return; // Safe relative path

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                !string.IsNullOrWhiteSpace(uri.Host))
                return;

            throw new BusinessRuleException("INVALID_POSTER_URL",
                "Đường dẫn poster phải là URL HTTP/HTTPS hợp lệ hoặc đường dẫn tương đối bắt đầu bằng '/'.");
        }
    }

    private static void ValidateProvenanceInputs(MovieProvenanceEditInput? provenance)
    {
        if (provenance is null) return;

        if (provenance.OriginalTitle is not null && provenance.OriginalTitle.Trim().Length > 250)
            throw new BusinessRuleException("INVALID_ORIGINAL_TITLE", "Tên gốc không được vượt quá 250 ký tự.");

        if (provenance.ReleaseYear.HasValue && provenance.ReleaseYear.Value is < 1888 or > 2100)
            throw new BusinessRuleException("INVALID_RELEASE_YEAR", "Năm phát hành phải nằm trong khoảng 1888 đến 2100.");

        if (provenance.DirectorNames is not null && provenance.DirectorNames.Trim().Length > 300)
            throw new BusinessRuleException("INVALID_DIRECTORS", "Tên đạo diễn không được vượt quá 300 ký tự.");

        if (!string.IsNullOrWhiteSpace(provenance.WikidataQid) && !WikidataQidRegex.IsMatch(provenance.WikidataQid.Trim()))
            throw new BusinessRuleException("INVALID_WIKIDATA_QID", "Mã Wikidata QID phải có định dạng Q theo sau bởi chữ số (ví dụ: Q496266).");

        if (!string.IsNullOrWhiteSpace(provenance.ImdbId) && !ImdbIdRegex.IsMatch(provenance.ImdbId.Trim()))
            throw new BusinessRuleException("INVALID_IMDB_ID", "Mã IMDb ID phải có định dạng tt theo sau bởi chữ số (ví dụ: tt0056923).");

        if (provenance.TmdbId.HasValue && provenance.TmdbId.Value <= 0)
            throw new BusinessRuleException("INVALID_TMDB_ID", "Mã TMDB ID phải là số nguyên dương.");

        if (!string.IsNullOrWhiteSpace(provenance.OfficialTrailerYoutubeId) && !YoutubeIdRegex.IsMatch(provenance.OfficialTrailerYoutubeId.Trim()))
            throw new BusinessRuleException("INVALID_YOUTUBE_ID", "Mã YouTube Trailer ID không hợp lệ (chỉ nhập Video ID, ví dụ: BJ_njww9SOQ).");

        if (!string.IsNullOrWhiteSpace(provenance.BackdropUrl))
        {
            ValidateSafeMediaPathOrUrl(provenance.BackdropUrl.Trim(), "INVALID_BACKDROP_URL", "Đường dẫn ảnh nền (Backdrop)");
        }
    }

    private static void ValidateSafeMediaPathOrUrl(string trimmed, string code, string label)
    {
        if (trimmed.Length > 500)
            throw new BusinessRuleException(code, $"{label} không được vượt quá 500 ký tự.");

        if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith("\\", StringComparison.Ordinal) ||
            trimmed.Contains('\\') ||
            trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(code, $"{label} không an toàn hoặc chứa ký tự không hợp lệ.");
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
            return;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrWhiteSpace(uri.Host))
            return;

        throw new BusinessRuleException(code, $"{label} phải là URL HTTP/HTTPS hợp lệ hoặc đường dẫn tương đối bắt đầu bằng '/'.");
    }
}
