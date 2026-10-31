using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BetaGiaiPhong.Tests.IntegrationTests;

public sealed class TestDatabaseFixture : IAsyncLifetime
{
    public const string DatabaseName = "BetaGiaiPhong_Integration_TestDb";
    public string ConnectionString { get; } = $"Server=localhost;Database={DatabaseName};Integrated Security=True;TrustServerCertificate=True;";
    public const string TestHmacKeyB64 = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";
    public const string TestQrAesKeyB64 = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";

    public DbContextOptions<BetaDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("BGP_MOCK_HMAC_KEY_B64", TestHmacKeyB64);
        Environment.SetEnvironmentVariable("BGP_QR_AES_KEY_B64", TestQrAesKeyB64);
        Environment.SetEnvironmentVariable("BGP_ALLOW_DEMO_SEED", "true");
        Environment.SetEnvironmentVariable("BGP_DEMO_PASSWORD", "DemoPassword123!");
        Environment.SetEnvironmentVariable("ConnectionStrings__BetaDb", ConnectionString);

        // Ensure database exists
        await using (var masterConn = new SqlConnection("Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;"))
        {
            await masterConn.OpenAsync();
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $@"
IF DB_ID('{DatabaseName}') IS NULL
BEGIN
    CREATE DATABASE [{DatabaseName}];
END;";
            await cmd.ExecuteNonQueryAsync();
        }

        Options = new DbContextOptionsBuilder<BetaDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsAssembly(typeof(BetaDbContext).Assembly.FullName))
            .Options;

        // Apply migrations
        await using var db = new BetaDbContext(Options);
        await db.Database.MigrateAsync();

        // Seed demo data if not seeded
        if (!await db.Users.AnyAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID"))
        {
            await DemoSeeder.SeedAsync(db);
        }

        // Clean up any historical dangling held seats from interrupted test runs to guarantee test isolation
        await db.Database.ExecuteSqlRawAsync(@"
            UPDATE dbo.ShowtimeSeats 
            SET state = 'AVAILABLE', owner_order_id = NULL, hold_expires_utc = NULL 
            WHERE state = 'HELD' 
              AND owner_order_id IN (SELECT order_id FROM dbo.Orders WHERE status IN ('CANCELLED', 'EXPIRED', 'FAILED'));");
    }

    public async Task<BetaDbContext> CreateDbContextAsync()
    {
        return new BetaDbContext(Options);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition("DatabaseCollection")]
public class DatabaseCollection : ICollectionFixture<TestDatabaseFixture>
{
}
