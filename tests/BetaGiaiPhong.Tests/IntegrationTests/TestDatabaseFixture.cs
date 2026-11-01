using System.Data;
using System.Text.RegularExpressions;
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
    public const string TestHmacKeyB64 = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";
    public const string TestQrAesKeyB64 = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";

    private static readonly HashSet<string> ProhibitedDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "master", "model", "msdb", "tempdb",
        "BetaGiaiPhong_Dev", "BetaGiaiPhongDev",
        "BetaGiaiPhong_Integration_TestDb",
        "BetaGiaiPhong_Prod", "BetaGiaiPhongProd"
    };

    public string DatabaseName { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;
    public DbContextOptions<BetaDbContext> Options { get; private set; } = null!;

    private bool _databaseCreatedByThisRun;
    private string? _createdDatabaseName;
    private string _masterConnectionString = null!;

    public async Task InitializeAsync()
    {
        ResolveMasterConnection();

        var allowCreate = Environment.GetEnvironmentVariable("BGP_TEST_ALLOW_DB_CREATE");
        if (string.Equals(allowCreate, "false", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Test database creation is disabled by BGP_TEST_ALLOW_DB_CREATE=false.");
        }

        var prefix = Environment.GetEnvironmentVariable("BGP_TEST_DATABASE_PREFIX") ?? "BetaGiaiPhong_Test_";
        if (!Regex.IsMatch(prefix, @"^[a-zA-Z0-9_]{1,32}$"))
        {
            throw new ArgumentException("BGP_TEST_DATABASE_PREFIX must be alphanumeric/underscore and 1 to 32 characters long.", nameof(prefix));
        }

        var existingDbName = Environment.GetEnvironmentVariable("BGP_TEST_DATABASE_NAME");
        if (!string.IsNullOrWhiteSpace(existingDbName) &&
            existingDbName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(existingDbName, @"^[a-zA-Z0-9_]{1,64}$") &&
            !ProhibitedDatabases.Contains(existingDbName))
        {
            DatabaseName = existingDbName;
        }
        else
        {
            var runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
            DatabaseName = $"{prefix}{runId}";
        }

        if (!Regex.IsMatch(DatabaseName, @"^[a-zA-Z0-9_]{1,64}$"))
        {
            throw new InvalidOperationException($"Generated database name '{DatabaseName}' is not a valid SQL Server identifier.");
        }

        if (ProhibitedDatabases.Contains(DatabaseName))
        {
            throw new InvalidOperationException($"Refusing to use protected database name: {DatabaseName}");
        }

        // Build target database connection string
        var testConnBuilder = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = DatabaseName
        };
        ConnectionString = testConnBuilder.ConnectionString;

        // Isolate process-wide environment variables for testing
        Environment.SetEnvironmentVariable("BGP_MOCK_HMAC_KEY_B64", TestHmacKeyB64);
        Environment.SetEnvironmentVariable("BGP_QR_AES_KEY_B64", TestQrAesKeyB64);
        Environment.SetEnvironmentVariable("BGP_ALLOW_DEMO_SEED", "true");
        Environment.SetEnvironmentVariable("BGP_DEMO_PASSWORD", "DemoPassword123!");
        Environment.SetEnvironmentVariable("ConnectionStrings__BetaDb", ConnectionString);
        Environment.SetEnvironmentVariable("BGP_TEST_DATABASE_NAME", DatabaseName);

        // Create ephemeral test database on SQL Server
        await using (var masterConn = new SqlConnection(_masterConnectionString))
        {
            await masterConn.OpenAsync();
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $@"
IF DB_ID('{DatabaseName}') IS NULL
BEGIN
    CREATE DATABASE [{DatabaseName}];
    ALTER DATABASE [{DatabaseName}] SET RECOVERY SIMPLE;
END;";
            await cmd.ExecuteNonQueryAsync();
        }

        _databaseCreatedByThisRun = true;
        _createdDatabaseName = DatabaseName;

        Options = new DbContextOptionsBuilder<BetaDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsAssembly(typeof(BetaDbContext).Assembly.FullName))
            .Options;

        // Apply migrations to freshly created test database
        await using var db = new BetaDbContext(Options);
        await db.Database.MigrateAsync();

        // Seed fresh synthetic demo data if not already present
        if (!await db.Users.AnyAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID"))
        {
            await DemoSeeder.SeedAsync(db);
        }
    }

    private void ResolveMasterConnection()
    {
        var explicitMaster = Environment.GetEnvironmentVariable("BGP_TEST_SQL_MASTER_CONNECTION");
        if (!string.IsNullOrWhiteSpace(explicitMaster))
        {
            ValidateConnectionSafety(explicitMaster);
            var builder = new SqlConnectionStringBuilder(explicitMaster)
            {
                InitialCatalog = "master"
            };
            _masterConnectionString = builder.ConnectionString;
            return;
        }

        var defaultDbConn = Environment.GetEnvironmentVariable("ConnectionStrings__BetaDb");
        if (!string.IsNullOrWhiteSpace(defaultDbConn))
        {
            ValidateConnectionSafety(defaultDbConn);
            var builder = new SqlConnectionStringBuilder(defaultDbConn)
            {
                InitialCatalog = "master"
            };
            _masterConnectionString = builder.ConnectionString;
            return;
        }

        // Default local SQL Server master connection
        _masterConnectionString = "Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;";
    }

    private static void ValidateConnectionSafety(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var catalog = builder.InitialCatalog;
        var server = builder.DataSource;

        if (catalog.Contains("prod", StringComparison.OrdinalIgnoreCase) ||
            server.Contains("prod", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Safety violation: Refusing to use production connection string for tests.");
        }

        if (catalog.Equals("BetaGiaiPhong_Dev", StringComparison.OrdinalIgnoreCase) ||
            catalog.Equals("BetaGiaiPhongDev", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Safety violation: Refusing to use active development database for tests. Set BGP_TEST_SQL_MASTER_CONNECTION to master.");
        }
    }

    public async Task<BetaDbContext> CreateDbContextAsync()
    {
        return new BetaDbContext(Options);
    }

    public async Task DisposeAsync()
    {
        var autoCleanup = Environment.GetEnvironmentVariable("BGP_TEST_AUTO_CLEANUP_DB");
        if (string.Equals(autoCleanup, "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_databaseCreatedByThisRun || string.IsNullOrWhiteSpace(_createdDatabaseName))
        {
            return;
        }

        var prefix = Environment.GetEnvironmentVariable("BGP_TEST_DATABASE_PREFIX") ?? "BetaGiaiPhong_Test_";
        if (!_createdDatabaseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            _createdDatabaseName != DatabaseName ||
            ProhibitedDatabases.Contains(_createdDatabaseName))
        {
            Console.Error.WriteLine($"[TestDatabaseFixture] Aborting cleanup: database name '{_createdDatabaseName}' failed ownership validation.");
            return;
        }

        try
        {
            SqlConnection.ClearAllPools();
            await using var masterConn = new SqlConnection(_masterConnectionString);
            await masterConn.OpenAsync();
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $@"
IF DB_ID('{_createdDatabaseName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{_createdDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{_createdDatabaseName}];
END;";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TestDatabaseFixture] Cleanup failed to drop test database '{_createdDatabaseName}': {ex.Message}");
        }
    }
}

[CollectionDefinition("DatabaseCollection")]
public class DatabaseCollection : ICollectionFixture<TestDatabaseFixture>
{
}
