using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PanelCalculator.Data.Security;

/// <summary>
/// Detects whether the existing application database is plain (unencrypted)
/// and, if so, migrates it to a SQLCipher-encrypted database in place.
///
/// Migration steps:
///   1. Probe the DB by opening it without a key and trying a trivial query.
///      - If that succeeds → DB is plain → proceed with migration.
///      - If it fails with "file is not a database" → already encrypted → noop.
///   2. Create a timestamped backup `<db>.bak-yyyyMMdd-HHmmss`.
///   3. Open the plain DB, ATTACH a new encrypted DB with the machine key,
///      call sqlcipher_export('encrypted'), detach, close.
///   4. Atomically replace the plain DB with the encrypted one.
///   5. On any failure: restore from backup and rethrow.
///   6. Old backups (&gt; 7 days) are auto-pruned.
///
/// All steps are logged to %AppData%\PanelCalculator\logs\migration-yyyy-MM-dd.log.
/// </summary>
public static class DbMigrator
{
    private const int BackupRetentionDays = 7;

    /// <summary>
    /// Result of the migration probe / run.
    /// </summary>
    public enum MigrationResult
    {
        /// <summary>Database already encrypted — nothing to do.</summary>
        AlreadyEncrypted,
        /// <summary>Database file did not exist yet — fresh install path, no migration needed.</summary>
        NoDatabase,
        /// <summary>Plain DB was detected and successfully migrated to encrypted.</summary>
        MigratedSuccessfully,
        /// <summary>Migration was attempted and failed; backup was restored.</summary>
        Failed
    }

    /// <summary>
    /// Run the migration if needed. Returns the outcome.
    /// </summary>
    /// <param name="dbPath">Absolute path to the production database file.</param>
    /// <param name="key">SQLCipher key (hex string) — typically <see cref="MachineKeyProvider.GetKey"/>.</param>
    /// <param name="logDir">Directory where migration logs are written. Created if missing.</param>
    public static MigrationResult MigrateIfNeeded(string dbPath, string key, string logDir)
    {
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(
            logDir,
            $"migration-{DateTime.Now:yyyy-MM-dd}.log");

        void Log(string msg)
        {
            try
            {
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
            }
            catch
            {
                // Logging failure must never break the app.
            }
        }

        if (!File.Exists(dbPath))
        {
            Log($"No existing DB at '{dbPath}'. Fresh install — encrypted DB will be created on first open.");
            PruneOldBackups(dbPath, Log);
            return MigrationResult.NoDatabase;
        }

        // Step 1: probe. Is this DB plain or already encrypted?
        Log($"Probing DB '{dbPath}'…");
        if (IsAlreadyEncrypted(dbPath, key, Log))
        {
            Log("DB is already encrypted (or not openable as plain). Skipping migration.");
            PruneOldBackups(dbPath, Log);
            return MigrationResult.AlreadyEncrypted;
        }

        Log("DB is plain (unencrypted). Beginning migration to encrypted form.");

        // Step 2: backup
        var backupPath = $"{dbPath}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Copy(dbPath, backupPath, overwrite: false);
            Log($"Backup created: '{backupPath}'");
        }
        catch (Exception ex)
        {
            Log($"FATAL: failed to create backup. {ex.GetType().Name}: {ex.Message}");
            throw new InvalidOperationException(
                "Tidak bisa membuat backup database sebelum enkripsi. Migrasi dibatalkan.", ex);
        }

        // Step 3 + 4: encrypt in-place
        var tempEncryptedPath = $"{dbPath}.encrypting-{Guid.NewGuid():N}";
        try
        {
            // Delete any leftover from prior aborted run
            if (File.Exists(tempEncryptedPath)) File.Delete(tempEncryptedPath);

            using (var src = new SqliteConnection($"Data Source={dbPath}"))
            {
                src.Open();

                using (var cmd = src.CreateCommand())
                {
                    // ATTACH with KEY → creates an encrypted DB; sqlcipher_export
                    // copies schema + data + indexes from main to the attached alias.
                    cmd.CommandText =
                        $"ATTACH DATABASE @path AS encrypted KEY @key;" +
                        $"SELECT sqlcipher_export('encrypted');" +
                        $"DETACH DATABASE encrypted;";
                    cmd.Parameters.AddWithValue("@path", tempEncryptedPath);
                    cmd.Parameters.AddWithValue("@key",  key);
                    cmd.ExecuteNonQuery();
                }
            }
            // All SqliteConnection instances must be disposed before file replace,
            // otherwise SQLite's pool keeps a file handle open.
            SqliteConnection.ClearAllPools();

            if (!File.Exists(tempEncryptedPath))
            {
                throw new InvalidOperationException(
                    "Proses enkripsi selesai tanpa error, tetapi file hasil tidak ditemukan.");
            }

            Log($"Encrypted DB written to temp path. Size = {new FileInfo(tempEncryptedPath).Length:N0} bytes.");

            // Step 4: swap (atomic-ish on Windows: delete + move)
            File.Delete(dbPath);
            File.Move(tempEncryptedPath, dbPath);
            Log($"Encrypted DB swapped into '{dbPath}'.");

            // Verify the swap by opening encrypted with the key
            VerifyEncrypted(dbPath, key);
            Log("Post-swap verification OK: DB opens with derived key and integrity check passes.");

            PruneOldBackups(dbPath, Log);
            return MigrationResult.MigratedSuccessfully;
        }
        catch (Exception ex)
        {
            Log($"ERROR during migration: {ex.GetType().Name}: {ex.Message}");
            // Step 5: rollback
            try
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(tempEncryptedPath))
                {
                    File.Delete(tempEncryptedPath);
                    Log("Removed half-written encrypted temp file.");
                }
                if (!File.Exists(dbPath) || new FileInfo(dbPath).Length == 0)
                {
                    File.Copy(backupPath, dbPath, overwrite: true);
                    Log($"Restored original DB from backup '{backupPath}'.");
                }
            }
            catch (Exception restoreEx)
            {
                Log($"CRITICAL: rollback failed. {restoreEx.GetType().Name}: {restoreEx.Message}");
                Log($"Backup remains at '{backupPath}'. Manual recovery required.");
            }
            throw;
        }
    }

    /// <summary>
    /// Probe the DB by opening it without a key and attempting a metadata read.
    /// Returns true if the read fails (suggesting the DB is already encrypted),
    /// false if it succeeds (plain DB).
    /// </summary>
    private static bool IsAlreadyEncrypted(string dbPath, string key, Action<string> log)
    {
        // First, try as plain (no key). If we can read sqlite_master → it's plain.
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            var n = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            log($"Plain probe succeeded ({n} sqlite_master rows). DB is plain.");
            return false;
        }
        catch (SqliteException ex)
        {
            log($"Plain probe failed: {ex.SqliteErrorCode}/{ex.SqliteExtendedErrorCode} {ex.Message}");
            // Common SQLCipher error when opening encrypted DB without key:
            // SQLITE_NOTADB (26) "file is not a database"
        }

        // Second, try with key. If this succeeds → DB is encrypted with our key.
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Password={key}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            var n = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            log($"Keyed probe succeeded ({n} sqlite_master rows). DB is encrypted with current key.");
            return true;
        }
        catch (SqliteException ex)
        {
            log($"Keyed probe ALSO failed: {ex.SqliteErrorCode}/{ex.SqliteExtendedErrorCode} {ex.Message}");
            // Worst case: DB encrypted with a different key (machine moved? identifier changed?).
            // We treat as "encrypted" — refuse to overwrite — caller should surface a clear error.
            throw new InvalidOperationException(
                "Database tidak bisa dibuka sebagai plain text maupun dengan kunci mesin saat ini. " +
                "Kemungkinan: database dibawa dari komputer lain, atau identitas hardware berubah " +
                "(motherboard / CPU / Windows Install ID). Hubungi support.", ex);
        }
    }

    private static void VerifyEncrypted(string dbPath, string key)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Password={key}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var result = cmd.ExecuteScalar()?.ToString();
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Verifikasi DB ter-enkripsi gagal: integrity_check = '{result}'.");
        }
    }

    /// <summary>
    /// Delete backup files older than <see cref="BackupRetentionDays"/> days.
    /// Pattern matched: "&lt;dbFileName&gt;.bak-*"
    /// </summary>
    private static void PruneOldBackups(string dbPath, Action<string> log)
    {
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            var name = Path.GetFileName(dbPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            var cutoff = DateTime.Now.AddDays(-BackupRetentionDays);
            var backups = Directory.GetFiles(dir, $"{name}.bak-*");
            foreach (var bak in backups)
            {
                try
                {
                    var mtime = File.GetLastWriteTime(bak);
                    if (mtime < cutoff)
                    {
                        File.Delete(bak);
                        log($"Pruned old backup '{bak}' (mtime {mtime:yyyy-MM-dd}).");
                    }
                }
                catch (Exception ex)
                {
                    log($"Could not prune backup '{bak}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            log($"Backup prune sweep failed: {ex.Message}");
        }
    }
}
