using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BetaGiaiPhong.Tests.IntegrationTests;

[Collection("DatabaseCollection")]
public sealed class TestDatabaseSafetyTests
{
    private static string GetMasterConnectionString()
    {
        var explicitMaster = Environment.GetEnvironmentVariable("BGP_TEST_SQL_MASTER_CONNECTION");
        if (!string.IsNullOrWhiteSpace(explicitMaster))
        {
            var builder = new SqlConnectionStringBuilder(explicitMaster)
            {
                InitialCatalog = "master"
            };
            return builder.ConnectionString;
        }

        return "Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;";
    }

    private static async Task<bool> DatabaseExistsAsync(string masterConnectionString, string dbName)
    {
        await using var conn = new SqlConnection(masterConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN DB_ID(@dbName) IS NOT NULL THEN 1 ELSE 0 END;";
        cmd.Parameters.Add(new SqlParameter("@dbName", SqlDbType.NVarChar, 128) { Value = dbName });
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
    }

    private static async Task ForceDropTestDatabaseAsync(string masterConnectionString, string dbName)
    {
        if (!dbName.StartsWith("BetaGiaiPhong_Test_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Test helper refuses to drop non-test database: {dbName}");
        }

        try
        {
            SqlConnection.ClearAllPools();
            await using var conn = new SqlConnection(masterConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
IF DB_ID(@dbName) IS NOT NULL
BEGIN
    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{dbName}];
END;";
            cmd.Parameters.Add(new SqlParameter("@dbName", SqlDbType.NVarChar, 128) { Value = dbName });
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Ignore cleanup errors in finally block
        }
    }

    [Fact]
    public async Task TC_SAFE_01_ExistingDatabase_IsNotDroppedOnDispose()
    {
        var masterConn = GetMasterConnectionString();
        var preExistingDb = $"BetaGiaiPhong_Test_PreExist_{Guid.NewGuid().ToString("N")[..8]}";

        try
        {
            await using (var conn = new SqlConnection(masterConn))
            {
                await conn.OpenAsync();
                await using (var createDbCmd = conn.CreateCommand())
                {
                    createDbCmd.CommandText = $@"
CREATE DATABASE [{preExistingDb}];
ALTER DATABASE [{preExistingDb}] SET RECOVERY SIMPLE;";
                    await createDbCmd.ExecuteNonQueryAsync();
                }

                await using (var createTableCmd = conn.CreateCommand())
                {
                    createTableCmd.CommandText = $@"
CREATE TABLE [{preExistingDb}].dbo.SafetySentinel (Id INT PRIMARY KEY, Marker NVARCHAR(64) NOT NULL);
INSERT INTO [{preExistingDb}].dbo.SafetySentinel (Id, Marker) VALUES (1, N'KEEP_ME_SAFE');";
                    await createTableCmd.ExecuteNonQueryAsync();
                }
            }

            Assert.True(await DatabaseExistsAsync(masterConn, preExistingDb));

            // 1a. Default behavior: refuses to use pre-existing database without explicit reuse opt-in
            var fixtureReject = new TestDatabaseFixture(new TestDatabaseOptions
            {
                MasterConnectionString = masterConn,
                AllowDbCreate = "true",
                AllowExistingDbReuse = "false",
                DatabaseName = preExistingDb,
                AutoCleanupDb = "true",
                UseIsolatedEnvironment = true
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fixtureReject.InitializeAsync());
            Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(fixtureReject.IsOwnedByThisRun);

            await fixtureReject.DisposeAsync();
            Assert.True(await DatabaseExistsAsync(masterConn, preExistingDb), "Pre-existing database must not be dropped after rejected init.");

            // 1b. With explicit non-destructive reuse opt-in: reuses database and never drops on DisposeAsync
            var fixtureReuse = new TestDatabaseFixture(new TestDatabaseOptions
            {
                MasterConnectionString = masterConn,
                AllowDbCreate = "false",
                AllowExistingDbReuse = "true",
                DatabaseName = preExistingDb,
                AutoCleanupDb = "true",
                UseIsolatedEnvironment = true
            });

            await fixtureReuse.InitializeAsync();
            Assert.False(fixtureReuse.IsOwnedByThisRun);
            Assert.Null(fixtureReuse.OwnershipToken);

            await fixtureReuse.DisposeAsync();
            Assert.True(await DatabaseExistsAsync(masterConn, preExistingDb), "Pre-existing database must not be dropped when reused.");

            // Verify sentinel table data is still intact
            await using (var conn = new SqlConnection(masterConn))
            {
                await conn.OpenAsync();
                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = $"SELECT Marker FROM [{preExistingDb}].dbo.SafetySentinel WHERE Id = 1;";
                var marker = Convert.ToString(await checkCmd.ExecuteScalarAsync());
                Assert.Equal("KEEP_ME_SAFE", marker);
            }
        }
        finally
        {
            await ForceDropTestDatabaseAsync(masterConn, preExistingDb);
        }
    }

    [Fact]
    public async Task TC_SAFE_02_NewlyCreatedDatabase_IsCleanedUpWhenAutoCleanupEnabled()
    {
        var masterConn = GetMasterConnectionString();
        var fixture = new TestDatabaseFixture(new TestDatabaseOptions
        {
            MasterConnectionString = masterConn,
            AllowDbCreate = "true",
            AutoCleanupDb = "true",
            UseIsolatedEnvironment = true
        });

        await fixture.InitializeAsync();
        var createdDbName = fixture.DatabaseName;

        try
        {
            Assert.True(fixture.IsOwnedByThisRun);
            Assert.False(string.IsNullOrWhiteSpace(fixture.OwnershipToken));
            Assert.True(await DatabaseExistsAsync(masterConn, createdDbName));

            // Verify ownership extended property exists on SQL Server
            await using (var conn = new SqlConnection(masterConn))
            {
                await conn.OpenAsync();
                await using var tokenCmd = conn.CreateCommand();
                tokenCmd.CommandText = $@"
SELECT CAST(value AS NVARCHAR(128))
FROM [{createdDbName}].sys.extended_properties
WHERE class = 0 AND name = N'{TestDatabaseFixture.OwnershipExtendedPropertyName}';";
                var dbToken = Convert.ToString(await tokenCmd.ExecuteScalarAsync());
                Assert.Equal(fixture.OwnershipToken, dbToken);
            }

            await fixture.DisposeAsync();
            Assert.False(await DatabaseExistsAsync(masterConn, createdDbName), "Newly created ephemeral database should be dropped on DisposeAsync.");
        }
        finally
        {
            await ForceDropTestDatabaseAsync(masterConn, createdDbName);
        }
    }

    [Fact]
    public async Task TC_SAFE_03_DatabaseWithMatchingPrefixButUnowned_IsNotDropped()
    {
        var masterConn = GetMasterConnectionString();
        var foreignDb = $"BetaGiaiPhong_Test_Foreign_{Guid.NewGuid().ToString("N")[..8]}";

        var fixture = new TestDatabaseFixture(new TestDatabaseOptions
        {
            MasterConnectionString = masterConn,
            AllowDbCreate = "true",
            AutoCleanupDb = "true",
            UseIsolatedEnvironment = true
        });

        await fixture.InitializeAsync();
        var ownedDb = fixture.DatabaseName;

        try
        {
            // Create a second database that shares the prefix 'BetaGiaiPhong_Test_' but belongs to another run
            await using (var conn = new SqlConnection(masterConn))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
CREATE DATABASE [{foreignDb}];
EXEC [{foreignDb}].sys.sp_addextendedproperty
    @name = N'{TestDatabaseFixture.OwnershipExtendedPropertyName}',
    @value = N'different_run_token_999999';";
                await cmd.ExecuteNonQueryAsync();

                // Also tamper with the ownedDb's extended property to simulate ownership mismatch
                await using var tamperCmd = conn.CreateCommand();
                tamperCmd.CommandText = $@"
EXEC [{ownedDb}].sys.sp_updateextendedproperty
    @name = N'{TestDatabaseFixture.OwnershipExtendedPropertyName}',
    @value = N'tampered_foreign_owner_token';";
                await tamperCmd.ExecuteNonQueryAsync();
            }

            await fixture.DisposeAsync();

            // Neither foreignDb nor the tampered ownedDb may be dropped because ownership token did not match
            Assert.True(await DatabaseExistsAsync(masterConn, foreignDb), "Database from another run with same prefix must never be dropped.");
            Assert.True(await DatabaseExistsAsync(masterConn, ownedDb), "Database with mismatched ownership token must not be dropped.");
        }
        finally
        {
            await ForceDropTestDatabaseAsync(masterConn, foreignDb);
            await ForceDropTestDatabaseAsync(masterConn, ownedDb);
        }
    }

    [Fact]
    public async Task TC_SAFE_04_WithoutExplicitOptIn_NoDatabaseIsCreated()
    {
        var masterConn = GetMasterConnectionString();
        var candidateDb = $"BetaGiaiPhong_Test_NoOptIn_{Guid.NewGuid().ToString("N")[..8]}";

        // 4a. AllowDbCreate is null (not opted in)
        var fixtureNullOptIn = new TestDatabaseFixture(new TestDatabaseOptions
        {
            MasterConnectionString = masterConn,
            AllowDbCreate = null,
            DatabaseName = candidateDb,
            UseIsolatedEnvironment = true
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixtureNullOptIn.InitializeAsync());
        await fixtureNullOptIn.DisposeAsync();
        Assert.False(await DatabaseExistsAsync(masterConn, candidateDb));

        // 4b. AllowDbCreate is "false"
        var fixtureFalseOptIn = new TestDatabaseFixture(new TestDatabaseOptions
        {
            MasterConnectionString = masterConn,
            AllowDbCreate = "false",
            DatabaseName = candidateDb,
            UseIsolatedEnvironment = true
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixtureFalseOptIn.InitializeAsync());
        await fixtureFalseOptIn.DisposeAsync();
        Assert.False(await DatabaseExistsAsync(masterConn, candidateDb));

        // 4c. Unconfirmed remote SQL Server is blocked
        var fixtureRemote = new TestDatabaseFixture(new TestDatabaseOptions
        {
            MasterConnectionString = "Server=unconfirmed-remote-sql.example.internal;Database=master;Integrated Security=True;",
            AllowDbCreate = "true",
            ConfirmRemoteSqlServer = "false",
            UseIsolatedEnvironment = true
        });

        var remoteEx = await Assert.ThrowsAsync<InvalidOperationException>(() => fixtureRemote.InitializeAsync());
        Assert.Contains("not a recognized local test instance", remoteEx.Message, StringComparison.OrdinalIgnoreCase);

        // 4d. Developer database name is strictly prohibited
        var fixtureDevDb = new TestDatabaseFixture(new TestDatabaseOptions
        {
            MasterConnectionString = masterConn,
            AllowDbCreate = "true",
            DatabaseName = "BetaGiaiPhong_Dev",
            UseIsolatedEnvironment = true
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixtureDevDb.InitializeAsync());
    }

    [Fact]
    public async Task TC_SAFE_05_InitializationFailure_DoesNotDeleteExistingData()
    {
        var masterConn = GetMasterConnectionString();
        var existingDb = $"BetaGiaiPhong_Test_FailInit_{Guid.NewGuid().ToString("N")[..8]}";

        try
        {
            await using (var conn = new SqlConnection(masterConn))
            {
                await conn.OpenAsync();
                await using (var createDbCmd = conn.CreateCommand())
                {
                    createDbCmd.CommandText = $@"
CREATE DATABASE [{existingDb}];
ALTER DATABASE [{existingDb}] SET RECOVERY SIMPLE;";
                    await createDbCmd.ExecuteNonQueryAsync();
                }

                await using (var createTableCmd = conn.CreateCommand())
                {
                    createTableCmd.CommandText = $@"
CREATE TABLE [{existingDb}].dbo.ExistingRecords (RecordId INT PRIMARY KEY, Payload NVARCHAR(100) NOT NULL);
INSERT INTO [{existingDb}].dbo.ExistingRecords (RecordId, Payload) VALUES (42, N'CRITICAL_EXISTING_DATA');";
                    await createTableCmd.ExecuteNonQueryAsync();
                }
            }

            // Simulate failure mid-initialization on an existing database
            var failingReuseFixture = new TestDatabaseFixture(new TestDatabaseOptions
            {
                MasterConnectionString = masterConn,
                AllowDbCreate = "false",
                AllowExistingDbReuse = "true",
                DatabaseName = existingDb,
                AutoCleanupDb = "true",
                UseIsolatedEnvironment = true,
                PostCreateHookAsync = _ => throw new InvalidOperationException("Simulated mid-initialization failure.")
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => failingReuseFixture.InitializeAsync());
            await failingReuseFixture.DisposeAsync();

            // Verify existing database and its records were NOT dropped or wiped
            Assert.True(await DatabaseExistsAsync(masterConn, existingDb), "Existing database must survive failed initialization and DisposeAsync.");

            await using (var conn = new SqlConnection(masterConn))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT Payload FROM [{existingDb}].dbo.ExistingRecords WHERE RecordId = 42;";
                var payload = Convert.ToString(await cmd.ExecuteScalarAsync());
                Assert.Equal("CRITICAL_EXISTING_DATA", payload);
            }

            // Simulate failure mid-initialization on a newly created database: partial DB should be cleaned up
            string? partialDbName = null;
            var failingCreateFixture = new TestDatabaseFixture(new TestDatabaseOptions
            {
                MasterConnectionString = masterConn,
                AllowDbCreate = "true",
                AutoCleanupDb = "true",
                UseIsolatedEnvironment = true,
                PostCreateHookAsync = _ => throw new InvalidOperationException("Simulated post-create migration failure.")
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => failingCreateFixture.InitializeAsync());
            partialDbName = failingCreateFixture.DatabaseName;
            await failingCreateFixture.DisposeAsync();

            Assert.False(await DatabaseExistsAsync(masterConn, partialDbName), "Partially initialized new database should be cleaned up on failure.");
        }
        finally
        {
            await ForceDropTestDatabaseAsync(masterConn, existingDb);
        }
    }
}
