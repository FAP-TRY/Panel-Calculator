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

    /// <summary>
    /// Regression test for the v1.2.5 bug where rebuilding Products via
    /// "RENAME old → temp; CREATE new; INSERT; DROP temp" left FK references
    /// in EstimationDetails pointing at the dropped temp table. The fix
    /// switched to "CREATE new; INSERT; DROP old; RENAME new → old" which
    /// preserves FK references intact.
    /// </summary>
    [Fact]
    public void Migrate_WithEstimationDetailsFK_PreservesReferences()
    {
        var (conn, path) = OpenLegacyDb();
        try
        {
            // Add EstimationDetails with a FK to Products(ProductId).
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE Estimations (
    EstimationId    INTEGER PRIMARY KEY AUTOINCREMENT,
    EstimationNumber TEXT NOT NULL
);
CREATE TABLE EstimationDetails (
    DetailId      INTEGER PRIMARY KEY AUTOINCREMENT,
    EstimationId  INTEGER NOT NULL,
    ProductId     INTEGER NOT NULL,
    Quantity      INTEGER NOT NULL,
    FOREIGN KEY (EstimationId) REFERENCES Estimations(EstimationId),
    FOREIGN KEY (ProductId)    REFERENCES Products(ProductId)
);";
                cmd.ExecuteNonQuery();
            }

            // Enable FK enforcement (matches runtime behaviour)
            using (var fkCmd = conn.CreateCommand())
            {
                fkCmd.CommandText = "PRAGMA foreign_keys = ON";
                fkCmd.ExecuteNonQuery();
            }

            Insert(conn, "C60N", "Schneider", "Schneider C60N", 1);
            Insert(conn, "RCBO-25", "ABB",    "ABB RCBO 25A",   2);

            // Reference both products from EstimationDetails
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO Estimations (EstimationId, EstimationNumber) VALUES (1, 'EST-001');
INSERT INTO EstimationDetails (EstimationId, ProductId, Quantity) VALUES (1, 1, 5);
INSERT INTO EstimationDetails (EstimationId, ProductId, Quantity) VALUES (1, 2, 3);";
                cmd.ExecuteNonQuery();
            }

            var rep = ProductsIndexMigrator.Migrate(conn);
            Assert.False(rep.Skipped);

            // After migration, FK enforcement should still find both products.
            // The buggy old code left FKs dangling at 'Products_legacy_v123'
            // which got dropped, so foreign_key_check would report orphans.
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "PRAGMA foreign_key_check";
                using var rdr = checkCmd.ExecuteReader();
                int orphans = 0;
                while (rdr.Read()) orphans++;
                Assert.Equal(0, orphans);
            }

            // And EstimationDetails should still resolve product names via JOIN.
            using (var joinCmd = conn.CreateCommand())
            {
                joinCmd.CommandText = @"
SELECT COUNT(*) FROM EstimationDetails d
INNER JOIN Products p ON p.ProductId = d.ProductId";
                Assert.Equal(2, Convert.ToInt32(joinCmd.ExecuteScalar()));
            }
        }
        finally { CleanupDb(conn, path); }
    }
}
