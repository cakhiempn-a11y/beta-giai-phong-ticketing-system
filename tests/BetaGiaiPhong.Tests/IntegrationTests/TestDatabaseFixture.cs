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

public sealed record TestDatabaseOptions
{
    public string? MasterConnectionString { get; init; }
    public string? AllowDbCreate { get; init; }
    public string? AllowExistingDbReuse { get; init; }
    public string? DatabasePrefix { get; init; }
    public string? DatabaseName { get; init; }
    public string? AutoCleanupDb { get; init; }
    public string? ConfirmRemoteSqlServer { get; init; }
    public bool UseIsolatedEnvironment { get; init; }
    public Func<BetaDbContext, Task>? PostCreateHookAsync { get; init; }
}

public sealed class TestDatabaseFixture : IAsyncLifetime
{
    public const string TestHmacKeyB64 = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";
    public const string TestQrAesKeyB64 = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";
    public const string OwnershipExtendedPropertyName = "BGP_TestRunOwnershipToken";

    private static readonly HashSet<string> ProhibitedDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "master", "model", "msdb", "tempdb",
        "BetaGiaiPhong_Dev", "BetaGiaiPhongDev",
        "BetaGiaiPhong_Integration_TestDb",
        "BetaGiaiPhong_Prod", "BetaGiaiPhongProd"
    };

    private readonly TestDatabaseOptions? _options;

    public string DatabaseName { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;
    public DbContextOptions<BetaDbContext> Options { get; private set; } = null!;
    public bool IsOwnedByThisRun => _databaseCreatedByThisRun;
    public string? OwnershipToken => _ownershipToken;

    private bool _databaseCreatedByThisRun;
    private string? _createdDatabaseName;
    private string? _ownershipToken;
    private string _masterConnectionString = null!;

    public TestDatabaseFixture()
    {
    }

    internal TestDatabaseFixture(TestDatabaseOptions options)
    {
        _options = options;
    }

    private string? GetSetting(string envVarName, string? optionOverride)
    {
        if (_options is { UseIsolatedEnvironment: true })
        {
            return optionOverride;
        }

        return optionOverride ?? Environment.GetEnvironmentVariable(envVarName);
    }

    public async Task InitializeAsync()
    {
        _databaseCreatedByThisRun = false;
        _createdDatabaseName = null;
        _ownershipToken = null;

        var allowCreate = GetSetting("BGP_TEST_ALLOW_DB_CREATE", _options?.AllowDbCreate);
        var allowReuse = GetSetting("BGP_TEST_ALLOW_EXISTING_DB_REUSE", _options?.AllowExistingDbReuse);
        var requestedDbName = GetSetting("BGP_TEST_DATABASE_NAME", _options?.DatabaseName);

        var isCreateOptIn = string.Equals(allowCreate, "true", StringComparison.OrdinalIgnoreCase);
        var isReuseOptIn = string.Equals(allowReuse, "true", StringComparison.OrdinalIgnoreCase) &&
                           !string.IsNullOrWhiteSpace(requestedDbName);

        if (!isCreateOptIn && !isReuseOptIn)
        {
            throw new InvalidOperationException(
                "Safety violation: Test database operations require explicit opt-in. " +
                "Set BGP_TEST_ALLOW_DB_CREATE=true to create an ephemeral test database, " +
                "or set BGP_TEST_DATABASE_NAME with BGP_TEST_ALLOW_EXISTING_DB_REUSE=true to reuse an existing test database.");
        }

        var prefix = GetSetting("BGP_TEST_DATABASE_PREFIX", _options?.DatabasePrefix) ?? "BetaGiaiPhong_Test_";
        if (!Regex.IsMatch(prefix, @"^[a-zA-Z0-9_]{1,32}$"))
        {
            throw new ArgumentException("BGP_TEST_DATABASE_PREFIX must be alphanumeric/underscore and 1 to 32 characters long.", nameof(prefix));
        }

        ResolveMasterConnection();

        if (!string.IsNullOrWhiteSpace(requestedDbName))
        {
            if (!Regex.IsMatch(requestedDbName, @"^[a-zA-Z0-9_]{1,64}$"))
            {
                throw new InvalidOperationException($"Requested database name '{requestedDbName}' is not a valid SQL Server identifier.");
            }

            if (ProhibitedDatabases.Contains(requestedDbName))
            {
                throw new InvalidOperationException($"Safety violation: Refusing to use protected database name '{requestedDbName}'.");
            }

            if (!requestedDbName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Safety violation: Requested database '{requestedDbName}' must start with configured test prefix '{prefix}'.");
            }

            DatabaseName = requestedDbName;
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
            throw new InvalidOperationException($"Safety violation: Refusing to use protected database name: {DatabaseName}");
        }

        var testConnBuilder = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = DatabaseName
        };
        ConnectionString = testConnBuilder.ConnectionString;

        await using (var masterConn = new SqlConnection(_masterConnectionString))
        {
            await masterConn.OpenAsync();

            bool dbExists;
            await using (var checkCmd = masterConn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT CASE WHEN DB_ID(@dbName) IS NOT NULL THEN 1 ELSE 0 END;";
                checkCmd.Parameters.Add(new SqlParameter("@dbName", SqlDbType.NVarChar, 128) { Value = DatabaseName });
                dbExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) == 1;
            }

            if (dbExists)
            {
                _databaseCreatedByThisRun = false;
                _createdDatabaseName = null;
                _ownershipToken = null;

                if (!isReuseOptIn)
                {
                    throw new InvalidOperationException(
                        $"Safety violation: Database '{DatabaseName}' already exists on SQL Server. " +
                        "Refusing to use or overwrite a pre-existing database without explicit BGP_TEST_ALLOW_EXISTING_DB_REUSE=true.");
                }
            }
            else
            {
                if (!isCreateOptIn)
                {
                    throw new InvalidOperationException(
                        $"Safety violation: Database '{DatabaseName}' does not exist and BGP_TEST_ALLOW_DB_CREATE is not explicitly set to 'true'.");
                }

                var token = Guid.NewGuid().ToString("N");

                await using (var createCmd = masterConn.CreateCommand())
                {
                    createCmd.CommandText = $"CREATE DATABASE [{DatabaseName}];";
                    await createCmd.ExecuteNonQueryAsync();
                }

                _databaseCreatedByThisRun = true;
                _createdDatabaseName = DatabaseName;
                _ownershipToken = token;

                try
                {
                    await using var configCmd = masterConn.CreateCommand();
                    configCmd.CommandText = $@"
ALTER DATABASE [{DatabaseName}] SET RECOVERY SIMPLE;
EXEC [{DatabaseName}].sys.sp_addextendedproperty
    @name = N'{OwnershipExtendedPropertyName}',
    @value = @token;";
                    configCmd.Parameters.Add(new SqlParameter("@token", SqlDbType.NVarChar, 128) { Value = token });
                    await configCmd.ExecuteNonQueryAsync();
                }
                catch
                {
                    await TryDropNewlyCreatedDatabaseOnInitFailureAsync(masterConn, DatabaseName);
                    _databaseCreatedByThisRun = false;
                    _createdDatabaseName = null;
                    _ownershipToken = null;
                    throw;
                }
            }
        }

        try
        {
            Environment.SetEnvironmentVariable("BGP_MOCK_HMAC_KEY_B64", TestHmacKeyB64);
            Environment.SetEnvironmentVariable("BGP_QR_AES_KEY_B64", TestQrAesKeyB64);
            Environment.SetEnvironmentVariable("BGP_ALLOW_DEMO_SEED", "true");
            Environment.SetEnvironmentVariable("BGP_DEMO_PASSWORD", "DemoPassword123!");

            Options = new DbContextOptionsBuilder<BetaDbContext>()
                .UseSqlServer(ConnectionString, sql => sql.MigrationsAssembly(typeof(BetaDbContext).Assembly.FullName))
                .Options;

            await using var db = new BetaDbContext(Options);
            await db.Database.MigrateAsync();

            if (_options?.PostCreateHookAsync is not null)
            {
                await _options.PostCreateHookAsync(db);
            }

            if (!await db.Users.AnyAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID"))
            {
                await DemoSeeder.SeedAsync(db);
            }

            if (_options is not { UseIsolatedEnvironment: true })
            {
                Environment.SetEnvironmentVariable("ConnectionStrings__BetaDb", ConnectionString);
                Environment.SetEnvironmentVariable("BGP_TEST_DATABASE_NAME", DatabaseName);
                Environment.SetEnvironmentVariable("BGP_TEST_ALLOW_EXISTING_DB_REUSE", "true");
            }
        }
        catch
        {
            if (_databaseCreatedByThisRun && !string.IsNullOrWhiteSpace(_createdDatabaseName) && !string.IsNullOrWhiteSpace(_ownershipToken))
            {
                await DisposeAsync();
            }

            throw;
        }
    }

    private void ResolveMasterConnection()
    {
        var explicitMaster = GetSetting("BGP_TEST_SQL_MASTER_CONNECTION", _options?.MasterConnectionString);
        var confirmRemote = GetSetting("BGP_TEST_CONFIRM_REMOTE_SQL_SERVER", _options?.ConfirmRemoteSqlServer);
        var isRemoteConfirmed = string.Equals(confirmRemote, "true", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(explicitMaster))
        {
            ValidateConnectionSafety(explicitMaster, isRemoteConfirmed);
            var builder = new SqlConnectionStringBuilder(explicitMaster)
            {
                InitialCatalog = "master"
            };
            _masterConnectionString = builder.ConnectionString;
            return;
        }

        const string defaultLocalMaster = "Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;";
        ValidateConnectionSafety(defaultLocalMaster, isRemoteConfirmed: false);
        _masterConnectionString = defaultLocalMaster;
    }

    private static void ValidateConnectionSafety(string connectionString, bool isRemoteConfirmed)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var catalog = builder.InitialCatalog ?? string.Empty;
        var server = builder.DataSource ?? string.Empty;

        string[] forbiddenMarkers = ["prod", "production", "staging", "live"];
        foreach (var marker in forbiddenMarkers)
        {
            if (catalog.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                server.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Safety violation: Refusing to use connection string containing '{marker}' for tests.");
            }
        }

        if (!string.IsNullOrWhiteSpace(catalog) &&
            !catalog.Equals("master", StringComparison.OrdinalIgnoreCase) &&
            ProhibitedDatabases.Contains(catalog))
        {
            throw new InvalidOperationException(
                $"Safety violation: Refusing to use protected database '{catalog}' in test master connection.");
        }

        if (!IsLocalSqlServerDataSource(server) && !isRemoteConfirmed)
        {
            throw new InvalidOperationException(
                $"Safety violation: SQL Server data source '{server}' is not a recognized local test instance. " +
                "To run tests against a remote test server, explicitly set BGP_TEST_CONFIRM_REMOTE_SQL_SERVER=true.");
        }
    }

    private static bool IsLocalSqlServerDataSource(string server)
    {
        var trimmed = server.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        string[] localPrefixes = ["localhost", "127.0.0.1", ".", "(local)", "(localdb)"];
        foreach (var prefix in localPrefixes)
        {
            if (trimmed.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(prefix + ",", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task TryDropNewlyCreatedDatabaseOnInitFailureAsync(SqlConnection masterConn, string dbName)
    {
        try
        {
            await using var dropCmd = masterConn.CreateCommand();
            dropCmd.CommandText = $@"
IF DB_ID(@dbName) IS NOT NULL
BEGIN
    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{dbName}];
END;";
            dropCmd.Parameters.Add(new SqlParameter("@dbName", SqlDbType.NVarChar, 128) { Value = dbName });
            await dropCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TestDatabaseFixture] Failed to clean up partially created database '{dbName}': {ex.Message}");
        }
    }

    public Task<BetaDbContext> CreateDbContextAsync()
    {
        return Task.FromResult(new BetaDbContext(Options));
    }

    public async Task DisposeAsync()
    {
        var autoCleanup = GetSetting("BGP_TEST_AUTO_CLEANUP_DB", _options?.AutoCleanupDb);
        if (string.Equals(autoCleanup, "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_databaseCreatedByThisRun ||
            string.IsNullOrWhiteSpace(_createdDatabaseName) ||
            string.IsNullOrWhiteSpace(_ownershipToken) ||
            string.IsNullOrWhiteSpace(_masterConnectionString))
        {
            return;
        }

        var prefix = GetSetting("BGP_TEST_DATABASE_PREFIX", _options?.DatabasePrefix) ?? "BetaGiaiPhong_Test_";
        if (!_createdDatabaseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            _createdDatabaseName != DatabaseName ||
            ProhibitedDatabases.Contains(_createdDatabaseName) ||
            !Regex.IsMatch(_createdDatabaseName, @"^[a-zA-Z0-9_]{1,64}$"))
        {
            Console.Error.WriteLine($"[TestDatabaseFixture] Aborting cleanup: database name '{_createdDatabaseName}' failed validation.");
            return;
        }

        var dbToDrop = _createdDatabaseName;
        var expectedToken = _ownershipToken;

        try
        {
            SqlConnection.ClearAllPools();
            await using var masterConn = new SqlConnection(_masterConnectionString);
            await masterConn.OpenAsync();

            bool exists;
            await using (var existsCmd = masterConn.CreateCommand())
            {
                existsCmd.CommandText = "SELECT CASE WHEN DB_ID(@dbName) IS NOT NULL THEN 1 ELSE 0 END;";
                existsCmd.Parameters.Add(new SqlParameter("@dbName", SqlDbType.NVarChar, 128) { Value = dbToDrop });
                exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync()) == 1;
            }

            if (!exists)
            {
                _databaseCreatedByThisRun = false;
                return;
            }

            string? actualToken;
            await using (var tokenCmd = masterConn.CreateCommand())
            {
                tokenCmd.CommandText = $@"
SELECT CAST(value AS NVARCHAR(128))
FROM [{dbToDrop}].sys.extended_properties
WHERE class = 0 AND name = N'{OwnershipExtendedPropertyName}';";
                var scalar = await tokenCmd.ExecuteScalarAsync();
                actualToken = scalar == null || scalar == DBNull.Value ? null : Convert.ToString(scalar);
            }

            if (!string.Equals(actualToken, expectedToken, StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    $"[TestDatabaseFixture] Aborting cleanup: database '{dbToDrop}' ownership token mismatch. Refusing to drop unowned database.");
                return;
            }

            await using var dropCmd = masterConn.CreateCommand();
            dropCmd.CommandText = $@"
ALTER DATABASE [{dbToDrop}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [{dbToDrop}];";
            await dropCmd.ExecuteNonQueryAsync();

            _databaseCreatedByThisRun = false;
            _createdDatabaseName = null;
            _ownershipToken = null;

            if (_options is not { UseIsolatedEnvironment: true } &&
                string.Equals(Environment.GetEnvironmentVariable("BGP_TEST_DATABASE_NAME"), dbToDrop, StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("BGP_TEST_DATABASE_NAME", null);
                Environment.SetEnvironmentVariable("BGP_TEST_ALLOW_EXISTING_DB_REUSE", null);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TestDatabaseFixture] Cleanup failed to drop test database '{dbToDrop}': {ex.Message}");
        }
    }
}

[CollectionDefinition("DatabaseCollection")]
public class DatabaseCollection : ICollectionFixture<TestDatabaseFixture>
{
}
