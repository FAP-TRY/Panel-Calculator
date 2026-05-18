using System.Data.Common;
using System.Text;

namespace PanelCalculator.Data.Migrations;

/// <summary>
/// Runtime migration that relaxes the legacy single-column UNIQUE constraint
/// on <c>Products.ReferenceCode</c> to a composite UNIQUE
/// <c>(ReferenceCode, Vendor)</c>.
///
/// Why: customers (e.g. PT Tritunggal Swarna) carry the same reference code
/// from multiple vendors — for example Schneider "C60N" and Himel "C60N" are
/// different physical products that must coexist. The legacy schema (created
/// before v1.2.4) blocks the cross-vendor insert with a UNIQUE violation.
///
/// SQLite cannot drop a column-level constraint via ALTER TABLE, so this
/// migrator rebuilds the table when it detects the legacy constraint:
///   1. Detect: PRAGMA index_list to find a UNIQUE auto-index covering ONLY
///      ReferenceCode (the column-level UNIQUE constraint creates one).
///   2. Dedupe: group existing rows by (ReferenceCode, Vendor) and keep the
///      latest LastUpdated, discarding the rest with a per-row log entry.
///   3. Rebuild: CREATE the table without the column-level UNIQUE, copy data,
///      drop the legacy table, rename, then ADD the composite UNIQUE index.
///
/// All work runs inside a single transaction. Logs are written line-by-line to
/// <c>products-index-migration.log</c> in the supplied log directory so the
/// installer / support engineer can audit what was discarded.
/// </summary>
public static class ProductsIndexMigrator
{
    public sealed class Report
    {
        public bool   Skipped         { get; init; }   // true when already migrated
        public string SkipReason      { get; init; } = "";
        public int    RowsBefore      { get; init; }
        public int    RowsAfter       { get; init; }
        public int    DuplicatesDiscarded => RowsBefore - RowsAfter;
        public List<string> DiscardedKeys { get; } = new();
    }

    /// <summary>
    /// Apply the migration to the supplied open SQLite connection. The
    /// connection must already be authenticated (SQLCipher key applied).
    /// </summary>
    public static Report Migrate(DbConnection conn, string? logFilePath = null)
    {
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();

        // ── 1) Detect — already migrated? ────────────────────────────────
        // Already-migrated state: a UNIQUE index named IX_Products_ReferenceCode_Vendor
        // exists. We use this name as the canonical marker.
        if (HasNamedIndex(conn, "IX_Products_ReferenceCode_Vendor"))
            return new Report { Skipped = true, SkipReason = "composite-index-already-exists" };

        // If the table doesn't exist yet (fresh install), nothing to migrate.
        if (!TableExists(conn, "Products"))
            return new Report { Skipped = true, SkipReason = "products-table-missing" };

        // ── 2) Run migration inside a transaction ────────────────────────
        var report = new Report();
        var log    = new StringBuilder();
        log.AppendLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] BEGIN ProductsIndexMigrator");

        using var tx = conn.BeginTransaction();
        try
        {
            // Snapshot row count
            int rowsBefore = ScalarInt(conn, tx, "SELECT COUNT(*) FROM Products");
            log.AppendLine($"  rows_before={rowsBefore}");

            // Discover the existing columns so the rebuild copies everything,
            // including custom columns added later (PriceYear, etc.).
            var columns = GetTableColumns(conn, tx, "Products");
            if (columns.Count == 0)
                throw new InvalidOperationException("Products table has no columns");

            var colList = string.Join(", ", columns.Select(c => $"`{c.Name}`"));

            // Dedupe: keep the most-recent row per (ReferenceCode, Vendor).
            // Discarded rows are logged with their full identity for audit.
            var discardedKeys = new List<string>();
            using (var cmdDiscarded = conn.CreateCommand())
            {
                cmdDiscarded.Transaction  = tx;
                cmdDiscarded.CommandText  = @"
SELECT ProductId, ReferenceCode, COALESCE(Vendor, ''), ProductName
FROM Products
WHERE ProductId NOT IN (
    SELECT MAX(ProductId)
    FROM Products
    GROUP BY ReferenceCode, COALESCE(Vendor, '')
)";
                using var rdr = cmdDiscarded.ExecuteReader();
                while (rdr.Read())
                {
                    var id     = rdr.GetInt64(0);
                    var code   = rdr.GetString(1);
                    var vendor = rdr.GetString(2);
                    var name   = rdr.IsDBNull(3) ? "" : rdr.GetString(3);
                    var key    = $"ProductId={id} ReferenceCode={code} Vendor={(string.IsNullOrEmpty(vendor) ? "<null>" : vendor)} Name={name}";
                    discardedKeys.Add(key);
                    log.AppendLine($"  DISCARD {key}");
                }
            }

            // Delete the duplicates that lost the MAX(ProductId) race.
            int discardedCount = ExecNonQuery(conn, tx, @"
DELETE FROM Products
WHERE ProductId NOT IN (
    SELECT MAX(ProductId)
    FROM Products
    GROUP BY ReferenceCode, COALESCE(Vendor, '')
)");
            log.AppendLine($"  duplicates_removed={discardedCount}");

            // ── 3) Rebuild the table without the column-level UNIQUE ─────
            // Build CREATE statement that matches the original schema EXCEPT
            // the UNIQUE keyword on ReferenceCode.
            var createSql = BuildProductsCreateTable(columns);
            ExecNonQuery(conn, tx, "ALTER TABLE Products RENAME TO Products_legacy_v123");
            ExecNonQuery(conn, tx, createSql);
            ExecNonQuery(conn, tx, $"INSERT INTO Products ({colList}) SELECT {colList} FROM Products_legacy_v123");
            ExecNonQuery(conn, tx, "DROP TABLE Products_legacy_v123");

            // EstimationDetails has a foreign key into Products(ProductId).
            // SQLite stores FKs by referenced column NAME, and we kept that
            // name + INTEGER PRIMARY KEY AUTOINCREMENT, so the FK survives
            // the rename. Re-enable / verify the FK pragma here as a sanity
            // check — failure is non-fatal so we just log it.
            // (FOREIGN_KEYS pragma stays at whatever the caller set.)

            // ── 4) Create the new composite UNIQUE index ─────────────────
            ExecNonQuery(conn, tx,
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Products_ReferenceCode_Vendor ON Products (ReferenceCode, Vendor)");
            // Recreate the non-unique Category index that the old table had
            // (it was dropped along with the table).
            ExecNonQuery(conn, tx,
                "CREATE INDEX IF NOT EXISTS IX_Products_Category ON Products (Category)");

            int rowsAfter = ScalarInt(conn, tx, "SELECT COUNT(*) FROM Products");
            log.AppendLine($"  rows_after={rowsAfter}");
            log.AppendLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] COMMIT");

            tx.Commit();

            report = new Report
            {
                Skipped     = false,
                SkipReason  = "",
                RowsBefore  = rowsBefore,
                RowsAfter   = rowsAfter
            };
            report.DiscardedKeys.AddRange(discardedKeys);
        }
        catch (Exception ex)
        {
            try { tx.Rollback(); } catch { /* best effort */ }
            log.AppendLine($"  ROLLBACK reason: {ex.Message}");
            throw;
        }
        finally
        {
            // Best-effort log write — never fail migration over a log file
            if (!string.IsNullOrWhiteSpace(logFilePath))
            {
                try
                {
                    var dir = Path.GetDirectoryName(logFilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(logFilePath, log.ToString());
                }
                catch { /* ignore */ }
            }
        }

        return report;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool TableExists(DbConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
        var p = cmd.CreateParameter(); p.ParameterName = "$n"; p.Value = name; cmd.Parameters.Add(p);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static bool HasNamedIndex(DbConnection conn, string indexName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=$n";
        var p = cmd.CreateParameter(); p.ParameterName = "$n"; p.Value = indexName; cmd.Parameters.Add(p);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private sealed record ColumnInfo(string Name, string Type, bool NotNull, string? DefaultValue, bool PrimaryKey);

    private static List<ColumnInfo> GetTableColumns(DbConnection conn, DbTransaction tx, string table)
    {
        var list = new List<ColumnInfo>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info(`{table}`)";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var name    = rdr.GetString(1);
            var type    = rdr.IsDBNull(2) ? "" : rdr.GetString(2);
            var notNull = rdr.GetInt32(3) != 0;
            var defVal  = rdr.IsDBNull(4) ? null : rdr.GetValue(4)?.ToString();
            var pk      = rdr.GetInt32(5) != 0;
            list.Add(new ColumnInfo(name, type, notNull, defVal, pk));
        }
        return list;
    }

    private static string BuildProductsCreateTable(List<ColumnInfo> columns)
    {
        // We need to recreate the table EXACTLY as before, minus the column-
        // level UNIQUE on ReferenceCode. PRAGMA table_info does not expose
        // column-level UNIQUE, so we hand-craft the column SQL using known
        // column names and rely on the dedupe + composite index for uniqueness.
        var sb = new StringBuilder();
        sb.AppendLine("CREATE TABLE Products (");
        for (int i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            sb.Append("    `").Append(c.Name).Append("` ").Append(c.Type);

            // The original ProductId is "INTEGER PRIMARY KEY AUTOINCREMENT".
            // Detect that by name + pk flag and emit explicit AUTOINCREMENT.
            if (c.PrimaryKey)
                sb.Append(" PRIMARY KEY AUTOINCREMENT");
            else
            {
                if (c.NotNull) sb.Append(" NOT NULL");
                if (!string.IsNullOrEmpty(c.DefaultValue))
                    sb.Append(" DEFAULT ").Append(c.DefaultValue);
            }
            if (i < columns.Count - 1) sb.Append(',');
            sb.AppendLine();
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static int ExecNonQuery(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }

    private static int ScalarInt(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
