using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Services;

public sealed class MovieManagementService(BetaDbContext db, ISystemClock clock)
{
    private static readonly HashSet<string> AllowedAgeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "P", "K", "T13", "T16", "T18", "C"
    };
    public async Task<long> CreateMovieAsync(
        long managerId,
        string title,
        short durationMin,
        string? genreText,
        string? ageLabel,
        string? synopsis,
        string? posterUrl,
        CancellationToken ct = default)
    {
        ValidateMovieInputs(title, durationMin, genreText, ageLabel, posterUrl);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await SecurityRules.EnsureRoleAsync(db, managerId, ct, "MANAGER", "ADMIN");

        var now = clock.UtcNow;
        var movie = new Movie
        {
            Title = title.Trim(),
            DurationMin = durationMin,
            GenreText = string.IsNullOrWhiteSpace(genreText) ? null : genreText.Trim(),
            AgeLabel = string.IsNullOrWhiteSpace(ageLabel) ? null : ageLabel.Trim(),
            Synopsis = string.IsNullOrWhiteSpace(synopsis) ? null : synopsis.Trim(),
            PosterUrl = string.IsNullOrWhiteSpace(posterUrl) ? null : posterUrl.Trim(),
            Status = "DRAFT"
        };

        db.Movies.Add(movie);
        await db.SaveChangesAsync(ct);

        db.AuditLogs.Add(Auditing.Event(managerId, "MOVIE_CREATED", "Movie",
            movie.MovieId.ToString(), new { movie.Title, movie.DurationMin, movie.Status }, now));
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
        CancellationToken ct = default)
    {
        ValidateMovieInputs(title, durationMin, genreText, ageLabel, posterUrl);

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

        movie.Title = title.Trim();
        movie.DurationMin = durationMin;
        movie.GenreText = string.IsNullOrWhiteSpace(genreText) ? null : genreText.Trim();
        movie.AgeLabel = string.IsNullOrWhiteSpace(ageLabel) ? null : ageLabel.Trim();
        movie.Synopsis = string.IsNullOrWhiteSpace(synopsis) ? null : synopsis.Trim();
        movie.PosterUrl = string.IsNullOrWhiteSpace(posterUrl) ? null : posterUrl.Trim();

        var now = clock.UtcNow;
        db.AuditLogs.Add(Auditing.Event(managerId, "MOVIE_UPDATED", "Movie",
            movie.MovieId.ToString(), new { movie.Title, movie.DurationMin, movie.Status }, now));

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
}
