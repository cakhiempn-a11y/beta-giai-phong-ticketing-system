using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BetaGiaiPhong.Data.Services;

public sealed class BusinessRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface ISystemClock { DateTime UtcNow { get; } }
public sealed class SystemClock : ISystemClock { public DateTime UtcNow => DateTime.UtcNow; }

public static class OrderStates
{
    public const string Pending = "PENDING_PAYMENT";
    public const string Paid = "PAID";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
    public const string Expired = "EXPIRED";
    public const string Review = "NEEDS_REVIEW";
}

public static class SeatStates
{
    public const string Available = "AVAILABLE";
    public const string Held = "HELD";
    public const string Sold = "SOLD";
    public const string Blocked = "BLOCKED";
}

public static class Auditing
{
    public static AuditLog Event(long? userId, string eventType, string entityType, string entityKey, object details, DateTime now)
        => new()
        {
            ActorUserId = userId, EventType = eventType, EntityType = entityType,
            EntityKey = entityKey, CorrelationId = Guid.NewGuid(),
            DetailsJson = JsonSerializer.Serialize(details), CreatedUtc = now
        };
}

// Cross-process serialization contract: all booking, expiration and payment mutations
// acquire the same SHOWTIME row lock BEFORE touching Orders / ShowtimeSeats / Tickets.
public static class SqlServerLocks
{
    public static Task<bool> ShowtimeAsync(BetaDbContext db, long showtimeId, CancellationToken ct = default)
        => ExistsWithUpdateLockAsync(db,
            "SELECT [showtime_id] FROM dbo.[Showtimes] WITH (UPDLOCK, HOLDLOCK) WHERE [showtime_id] = @id",
            showtimeId, ct);

    public static Task<bool> RoomAsync(BetaDbContext db, long roomId, CancellationToken ct = default)
        => ExistsWithUpdateLockAsync(db,
            "SELECT [room_id] FROM dbo.[Rooms] WITH (UPDLOCK, HOLDLOCK) WHERE [room_id] = @id",
            roomId, ct);

    public static Task<bool> MovieAsync(BetaDbContext db, long movieId, CancellationToken ct = default)
        => ExistsWithUpdateLockAsync(db,
            "SELECT [movie_id] FROM dbo.[Movies] WITH (UPDLOCK, HOLDLOCK) WHERE [movie_id] = @id",
            movieId, ct);

    public static async Task<long?> TicketByHashAsync(BetaDbContext db, byte[] hash, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("An active SQL Server transaction is required.");
        command.CommandText = "SELECT [ticket_id] FROM dbo.[Tickets] WITH (UPDLOCK, HOLDLOCK) WHERE [qr_token_hash] = @hash";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@hash";
        parameter.DbType = DbType.Binary;
        parameter.Size = 32;
        parameter.Value = hash;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task<bool> ExistsWithUpdateLockAsync(BetaDbContext db, string sql, long id, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("An active SQL Server transaction is required.");
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.DbType = DbType.Int64;
        parameter.Value = id;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(ct);
        return result is not null and not DBNull;
    }
}

public static class SecurityRules
{
    public static async Task EnsureRoleAsync(BetaDbContext db, long userId, CancellationToken ct, params string[] roles)
    {
        var active = await db.Users.AnyAsync(u => u.UserId == userId && u.Status == "ACTIVE", ct);
        var authorized = await (from ur in db.UserRoles
            join role in db.Roles on ur.RoleId equals role.RoleId
            where ur.UserId == userId && roles.Contains(role.RoleCode)
            select ur).AnyAsync(ct);
        if (!active || !authorized)
            throw new BusinessRuleException("FORBIDDEN", "User is disabled or lacks the required role.");
    }
}
