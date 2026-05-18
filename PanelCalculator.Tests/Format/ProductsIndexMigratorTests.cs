using Microsoft.Data.Sqlite;
using PanelCalculator.Data.Migrations;
using Xunit;

namespace PanelCalculator.Tests.Format;

/// <summary>
/// SQLite-backed tests that prove the ProductsIndexMigrator actually rebuilds
/// the legacy table and allows the cross-vendor insert that previously
/// violated the single-column UNIQUE on ReferenceCode.
///
/// We open an in-memory SQLite DB and seed it with the *legacy* schema (the
/// same DDL shipped in v1.2.3 / 001_InitialCreate.sql), insert duplicates,
/// then run the migrator and assert the post-state.
/// </summary>
public class ProductsIndexMigratorTests
{
    // ── Legacy v1.2.3 schema reproduction ────────────────────────────────
    // Same column set as 001_InitialCreate.sql plus the columns that the
    // runtime MigrateDatabase() added later (PriceYear, etc.) — verifies
    // the rebuild preserves extra columns.
    private const string LegacyCreateSql = @"
CREATE TABLE Products (
    ProductId      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Category       TEXT    NOT NULL,
    ReferenceCode  TEXT    NOT NULL UNIQUE,
    ProductName    TEXT    NOT NULL,
    Specifications TEXT,
    Price          TEXT    NOT NULL,
    StockStatus    INTEGER NOT NULL,
    Vendor         TEXT,
    LastUpdated    TEXT    NOT NULL,
    PriceYear      INTEGER NULL
);
CREATE INDEX IX_Products_Category ON Products (Category);";

    private static (SqliteConnection conn, string path) OpenLegacyDb()
    {
        // Use a temp file rather than :memory: so the schema patch trick used
        // by Migrate_DedupesCompositeDuplicates_KeepsLatest survives a close/
        // reopen cycle (needed to force SQLite to refresh its schema cache).
        var path = Path.Combine(Path.GetTempPath(),
            "pc-migrator-test-" + Guid.NewGuid().ToString("N") + ".db");
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = LegacyCreateSql;
        cmd.ExecuteNonQuery();
        return (conn, path);
    }

    private static void CleanupDb(SqliteConnection conn, string path)
    {
        try { conn.Close(); } catch { }
        try { conn.Dispose(); } catch { }
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static void Insert(SqliteConnection conn, string code, string vendor, string name, int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Products (ProductId, Category, ReferenceCode, ProductName, Price, StockStatus, Vendor, LastUpdated)
VALUES ($id, 'MCB', $code, $name, '100', 1, $vendor, datetime('now'))";
        cmd.Parameters.AddWithValue("$id",     productId);
        cmd.Parameters.AddWithValue("$code",   code);
        cmd.Parameters.AddWithValue("$name",   name);
        cmd.Parameters.AddWithValue("$vendor", (object?)vendor ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static int CountRows(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public void Migrate_LegacyTable_BuildsCompositeIndex()
    {
        var (conn, path) = OpenLegacyDb();
        try
        {
            Insert(conn, "C60N", "Schneider", "Schneider C60N", 1);

            var rep = ProductsIndexMigrator.Migrate(conn);
            Assert.False(rep.Skipped);
            Assert.Equal(1, rep.RowsBefore);
            Assert.Equal(1, rep.RowsAfter);

            // Composite index exists
            var idxCount = CountRows(conn,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_Products_ReferenceCode_Vendor'");
            Assert.Equal(1, idxCount);

            // Re-running is a no-op
            var rep2 = ProductsIndexMigrator.Migrate(conn);
            Assert.True(rep2.Skipped);
            Assert.Equal("composite-index-already-exists", rep2.SkipReason);
        }
        finally { CleanupDb(conn, path); }
    }

    [Fact]
    public void Migrate_AllowsCrossVendorAfterMigration()
    {
        var (conn, path) = OpenLegacyDb();
        try
        {
        Insert(conn, "C60N", "Schneider", "Schneider C60N", 1);

        ProductsIndexMigrator.Migrate(conn);

        // Cross-vendor insert that would have failed under the legacy UNIQUE
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
INSERT INTO Products (Category, ReferenceCode, ProductName, Price, StockStatus, Vendor, LastUpdated)
VALUES ('MCB', 'C60N', 'Himel C60N', '80', 1, 'Himel', datetime('now'))";
            cmd.ExecuteNonQuery();
        }

        Assert.Equal(2, CountRows(conn, "SELECT COUNT(*) FROM Products WHERE ReferenceCode='C60N'"));

        // …but the same (ReferenceCode, Vendor) should still fail
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
INSERT INTO Products (Category, ReferenceCode, ProductName, Price, StockStatus, Vendor, LastUpdated)
VALUES ('MCB', 'C60N', 'Schneider C60N v2', '110', 1, 'Schneider', datetime('now'))";
            var ex = Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
            Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        }
        finally { CleanupDb(conn, path); }
    }

    [Fact]
    public void Migrate_DedupesCompositeDuplicates_KeepsLatest()
    {
        // For this test we need a Products table that ALLOWS duplicate
        // (ReferenceCode, Vendor) rows so we can simulate the corrupt state
        // that the migrator must clean up. SQLite cannot drop a column-level
        // UNIQUE constraint at runtime, so we build the table directly
        // without the UNIQUE keyword. From the migrator's perspective this
        // looks like a legacy table that's missing the new composite index,
        // which is exactly the condition it must handle.
        var path = Path.Combine(Path.GetTempPath(),
            "pc-migrator-dedupe-" + Guid.NewGuid().ToString("N") + ".db");
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        try
        {
            using (var c = conn.CreateCommand())
            {
                c.CommandText = @"
CREATE TABLE Products (
    ProductId      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Category       TEXT    NOT NULL,
    ReferenceCode  TEXT    NOT NULL,
    ProductName    TEXT    NOT NULL,
    Specifications TEXT,
    Price          TEXT    NOT NULL,
    StockStatus    INTEGER NOT NULL,
    Vendor         TEXT,
    LastUpdated    TEXT    NOT NULL,
    PriceYear      INTEGER NULL
);
CREATE INDEX IX_Products_Category ON Products (Category);";
                c.ExecuteNonQuery();
            }

        Insert(conn, "C60N", "Schneider", "Schneider C60N",       1);
        Insert(conn, "C60N", "Himel",     "Himel C60N",           2);
        Insert(conn, "ABC",  null!,       "Generic ABC",          3);
        Insert(conn, "C60N", "Schneider", "Schneider C60N OLDER copy", 4);

        var rep = ProductsIndexMigrator.Migrate(conn);
        Assert.False(rep.Skipped);
        Assert.Equal(4, rep.RowsBefore);
        Assert.Equal(3, rep.RowsAfter);   // one composite-dupe discarded
        Assert.Single(rep.DiscardedKeys);
        Assert.Contains("C60N", rep.DiscardedKeys[0]);

        // The row that survived is the MAX(ProductId), i.e. ProductId=4.
        using var verify = conn.CreateCommand();
        verify.CommandText = "SELECT ProductName FROM Products WHERE ReferenceCode='C60N' AND Vendor='Schneider'";
        var name = (string?)verify.ExecuteScalar();
        Assert.Equal("Schneider C60N OLDER copy", name);
        }
        finally { CleanupDb(conn, path); }
    }

    [Fact]
    public void Migrate_NoProductsTable_SkipsCleanly()
    {
        var name = "test-empty-" + Guid.NewGuid().ToString("N");
        using var conn = new SqliteConnection($"Data Source={name};Mode=Memory;Cache=Shared");
        conn.Open();

        var rep = ProductsIndexMigrator.Migrate(conn);
        Assert.True(rep.Skipped);
        Assert.Equal("products-table-missing", rep.SkipReason);
    }
}
