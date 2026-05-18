using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using PanelCalculator.Data.Security;
using Xunit;

namespace PanelCalculator.Tests.Security;

/// <summary>
/// End-to-end roundtrip: create a plain SQLite DB with sample data, run the
/// migrator, then verify (a) data is still readable, (b) DB is no longer
/// openable without the key.
/// </summary>
[SupportedOSPlatform("windows")]
public class DbMigratorTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dbPath;
    private readonly string _logDir;

    public DbMigratorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(),
            $"PanelCalcMigratorTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _dbPath = Path.Combine(_testDir, "PanelCalculator.db");
        _logDir = Path.Combine(_testDir, "logs");

        // Ensure SQLitePCL is initialised in test runner (no Program.Main here).
        SQLitePCL.Batteries_V2.Init();
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void MigrateIfNeeded_OnMissingDb_ReturnsNoDatabase()
    {
        var result = DbMigrator.MigrateIfNeeded(
            _dbPath, MachineKeyProvider.GetKey(), _logDir);

        Assert.Equal(DbMigrator.MigrationResult.NoDatabase, result);
    }

    [Fact]
    public void MigrateIfNeeded_OnPlainDb_EncryptsAndPreservesData()
    {
        // Arrange: create plain DB with one table + one row
        CreatePlainDbWithSampleData(_dbPath);
        Assert.True(CanOpenWithoutKey(_dbPath), "Precondition: plain DB readable without key.");

        var key = MachineKeyProvider.GetKey();

        // Act
        var result = DbMigrator.MigrateIfNeeded(_dbPath, key, _logDir);

        // Assert
        Assert.Equal(DbMigrator.MigrationResult.MigratedSuccessfully, result);
        Assert.False(CanOpenWithoutKey(_dbPath), "Post-migration: DB must NOT open without key.");

        // Data still intact under the new key
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _dbPath, Password = key }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name, Price FROM Products WHERE Id = 1;";
        using var rdr = cmd.ExecuteReader();
        Assert.True(rdr.Read());
        Assert.Equal("Schneider iC60N",   rdr.GetString(0));
        Assert.Equal(1_234_567L,          rdr.GetInt64(1));

        // Backup exists
        var backups = Directory.GetFiles(_testDir, "PanelCalculator.db.bak-*");
        Assert.NotEmpty(backups);
    }

    [Fact]
    public void MigrateIfNeeded_OnAlreadyEncryptedDb_ReturnsAlreadyEncrypted()
    {
        // Arrange: pre-encrypt
        CreatePlainDbWithSampleData(_dbPath);
        var key = MachineKeyProvider.GetKey();
        DbMigrator.MigrateIfNeeded(_dbPath, key, _logDir);

        // Act: run again
        var second = DbMigrator.MigrateIfNeeded(_dbPath, key, _logDir);

        // Assert: noop
        Assert.Equal(DbMigrator.MigrationResult.AlreadyEncrypted, second);
    }

    // ────────────────────────────────────────────────────────────────────────

    private static void CreatePlainDbWithSampleData(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Products (
                Id    INTEGER PRIMARY KEY,
                Name  TEXT NOT NULL,
                Price INTEGER NOT NULL
            );
            INSERT INTO Products (Id, Name, Price) VALUES (1, 'Schneider iC60N', 1234567);
        ";
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    private static bool CanOpenWithoutKey(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            _ = cmd.ExecuteScalar();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }
}
