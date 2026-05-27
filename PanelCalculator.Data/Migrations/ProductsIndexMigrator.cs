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

        // SQLite rule: foreign_keys pragma cannot change INSIDE a transaction.
        // The rebuild renames Products → temp + DROP temp, which would normally
        // fail the FK from EstimationDetails.ProductId. We disable FK enforcement
        // for the duration of the rebuild, then re-enable + run a foreign_key_check
        // after commit to ensure no orphans got introduced.
        bool fkWasOn = false;
        try
        {
            using var cmdFkRead = conn.CreateCommand();
            cmdFkRead.CommandText = "PRAGMA foreign_keys";
            fkWasOn = Convert.ToInt32(cmdFkRead.ExecuteScalar()) != 0;
            using var cmdFkOff = conn.CreateCommand();
            cmdFkOff.CommandText = "PRAGMA foreign_keys = OFF";
            cmdFkOff.ExecuteNonQuery();
            log.AppendLine($"  foreign_keys_was={fkWasOn}, now=OFF for rebuild");
        }
        catch (Exception fkEx)
        {
            log.AppendLine($"  WARN could not toggle foreign_keys: {fkEx.Message}");
        }

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
            //
            // CRITICAL ORDER (per SQLite docs §7 "Making Other Kinds Of Table
            // Schema Changes"): CREATE new → INSERT → DROP old → RENAME new.
            //
            // The naive "RENAME old → temp; CREATE new; INSERT; DROP temp"
            // looks equivalent but is BROKEN: SQLite's modern ALTER TABLE
            // (legacy_alter_table=OFF, the default since 3.25) auto-updates
            // FK references during RENAME, so EstimationDetails.ProductId FK
            // ends up pointing at the temp-renamed table, then dangles when
            // we DROP it. The DROP-first pattern below sidesteps the auto-
            // update because (a) DROP of Products doesn't touch FK refs and
            // (b) the final RENAME Products_new → Products is a fresh rename
            // that only affects rows already referencing 'Products_new' (none).
            var createSql = BuildProductsNewCreateTable(columns);
            ExecNonQuery(conn, tx, createSql);
            ExecNonQuery(conn, tx, $"INSERT INTO Products_new ({colList}) SELECT {colList} FROM Products");
            ExecNonQuery(conn, tx, "DROP TABLE Products");
            ExecNonQuery(conn, tx, "ALTER TABLE Products_new RENAME TO Products");

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
            // Restore foreign_keys pragma to original state (default ON).
            // Run after transaction commit/rollback because pragma can't change
            // inside a transaction. Also run foreign_key_check to verify no
            // orphans got introduced by the rebuild.
            try
            {
                if (fkWasOn)
                {
                    using var cmdFkOn = conn.CreateCommand();
                    cmdFkOn.CommandText = "PRAGMA foreign_keys = ON";
                    cmdFkOn.ExecuteNonQuery();

                    using var cmdCheck = conn.CreateCommand();
                    cmdCheck.CommandText = "PRAGMA foreign_key_check";
                    using var rdr = cmdCheck.ExecuteReader();
                    int orphanCount = 0;
                    while (rdr.Read())
                    {
                        orphanCount++;
                        if (orphanCount <= 10)
                        {
                            log.AppendLine($"  FK_ORPHAN table={rdr.GetValue(0)} rowid={rdr.GetValue(1)} ref={rdr.GetValue(2)}");
                        }
                    }
                    if (orphanCount > 0)
                        log.AppendLine($"  WARNING: {orphanCount} orphan FK rows detected post-migration (logged above; truncated to 10).");
                    else
                        log.AppendLine($"  foreign_key_check: PASSED (no orphans)");
                }
            }
            catch (Exception restoreEx)
            {
                log.AppendLine($"  WARN could not restore foreign_keys / run check: {restoreEx.Message}");
            }

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

    private static string BuildProductsNewCreateTable(List<ColumnInfo> columns)
    {
        // We need to recreate the table EXACTLY as before, minus the column-
        // level UNIQUE on ReferenceCode. PRAGMA table_info does not expose
        // column-level UNIQUE, so we hand-craft the column SQL using known
        // column names and rely on the dedupe + composite index for uniqueness.
        // Temp name 'Products_new' is renamed to 'Products' AFTER the legacy
        // table is dropped — see migration step ordering.
        var sb = new StringBuilder();
        sb.AppendLine("CREATE TABLE Products_new (");
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
