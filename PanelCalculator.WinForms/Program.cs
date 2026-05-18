using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PanelCalculator.Core.Models;
using PanelCalculator.Core.Security;
using PanelCalculator.Core.Services;
using PanelCalculator.Data;
using PanelCalculator.Data.Repositories;
using PanelCalculator.Data.Security;
using PanelCalculator.WinForms.Forms;
using PanelCalculator.WinForms.Services;
using System.Reflection;

namespace PanelCalculator.WinForms;

// [Obfuscation] on the class tells Obfuscar to skip renaming
// Main() because WinExe entry points must keep their signature.
[Obfuscation(Exclude = true, ApplyToMembers = false)]
static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // ── Initialize SQLitePCLRaw with the SQLCipher provider BEFORE any
        // SqliteConnection is opened. The bundle has a module initializer
        // that does this automatically when the assembly is loaded, but we
        // call Init() explicitly so the ordering is obvious and reliable.
        SQLitePCL.Batteries_V2.Init();

        // ── Encrypt-at-rest migration ────────────────────────────────────
        // Must run BEFORE the DbContext is touched. If the DB on disk is
        // plain (legacy install) we transparently re-encrypt it with a key
        // derived from this machine. If the migration fails we surface a
        // clear error to the user and exit instead of risking data loss.
        var dbPath = GetDbPath();
        var logDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "logs");
        try
        {
            DbMigrator.MigrateIfNeeded(dbPath, MachineKeyProvider.GetKey(), logDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Migrasi database (enkripsi) gagal.\n\n" +
                $"Detail: {ex.Message}\n\n" +
                $"Backup database lama tersimpan di:\n{Path.GetDirectoryName(dbPath)}\n\n" +
                "Aplikasi tidak bisa lanjut. Mohon hubungi support dan jangan hapus folder ini.",
                "Kalkulator Panel — Migrasi Gagal",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Setup DI Container
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // Initialize database
        using (var scope = serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PanelCalculatorContext>();
            context.Database.EnsureCreated();
            MigrateDatabase(context);
            SeedDefaultAdmin(context);
        }

        // ── Login → Shell loop ───────────────────────────────────────────
        // After an explicit logout (WantsRelogin = true) the shell is closed
        // and we return here to show the login screen again instead of exiting.
        while (true)
        {
            var loginForm = serviceProvider.GetRequiredService<LoginForm>();
            loginForm.ShowDialog();   // ShowDialog creates its own message loop

            if (!loginForm.LoginSuccess)
                break;  // user closed without logging in → exit app

            // ── License gate ─────────────────────────────────────────────
            // Runs AFTER login (so the admin/user is authenticated before
            // they see the activation screen — prevents a random walk-in from
            // probing the activation UI). On a dev machine the gate is
            // bypassed via DEBUG or the dev-bypass.flag file; in Release on a
            // customer machine the user MUST paste a valid license.
            using (var licenseScope = serviceProvider.CreateScope())
            {
                var licenseCtx = licenseScope.ServiceProvider.GetRequiredService<PanelCalculatorContext>();
                var gateResult = LicenseGate.RunGate(licenseCtx, msg =>
                {
                    // Best-effort warning log — never fail startup on logging issues.
                    try { File.AppendAllText(
                        Path.Combine(Path.GetDirectoryName(dbPath)!, "logs", "license-gate.log"),
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] WARN: {msg}{Environment.NewLine}");
                    } catch { /* ignore */ }
                    Console.WriteLine("[LicenseGate] " + msg);
                });

                if (gateResult == LicenseGate.Outcome.UserCancelled)
                {
                    loginForm.Dispose();
                    break;  // user closed activation form → exit app
                }
            }

            var shell = serviceProvider.GetRequiredService<ShellForm>();
            shell.CurrentUser = loginForm.LoggedInUser!;
            loginForm.Dispose();

            Application.Run(shell);  // blocks until shell is closed

            if (!shell.WantsRelogin)
                break;  // closed via X button (not logout) → exit app
            // else: loop back → show login screen again
        }
    }

    /// <summary>
    /// Manual column additions for DB schema evolution (SQLite only supports ADD COLUMN).
    /// Safe to run on every startup — each ALTER TABLE is silently ignored if column already exists.
    /// </summary>
    private static void MigrateDatabase(PanelCalculator.Data.PanelCalculatorContext context)
    {
        try
        {
            var conn = context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();

            // ── Products table: relax UNIQUE(ReferenceCode) to composite ──
            // (ReferenceCode, Vendor). Idempotent — skips when already done.
            // Runs BEFORE the ADD COLUMN block below so the rebuild copies
            // the legacy columns and the ADD COLUMN block tops up any new ones.
            try
            {
                var dbDir  = Path.GetDirectoryName(GetDbPath());
                var logDir = string.IsNullOrEmpty(dbDir) ? null : Path.Combine(dbDir, "logs");
                var logFile = logDir == null ? null : Path.Combine(logDir, "products-index-migration.log");
                PanelCalculator.Data.Migrations.ProductsIndexMigrator.Migrate(conn, logFile);
            }
            catch { /* non-fatal — schema upgrades below will continue best-effort */ }

            void TryExec(string sql)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                try { cmd.ExecuteNonQuery(); } catch { /* already exists – ignore */ }
            }

            // EstimationDetails new columns
            TryExec("ALTER TABLE EstimationDetails ADD COLUMN AdjPercent REAL NOT NULL DEFAULT 0");
            TryExec("ALTER TABLE EstimationDetails ADD COLUMN Section TEXT NOT NULL DEFAULT 'Material Utama'");
            TryExec("ALTER TABLE EstimationDetails ADD COLUMN Satuan TEXT NOT NULL DEFAULT 'pcs'");
            TryExec("ALTER TABLE EstimationDetails ADD COLUMN Adj2Percent REAL NOT NULL DEFAULT 0");
            TryExec("ALTER TABLE EstimationDetails ADD COLUMN Adj3Percent REAL NOT NULL DEFAULT 0");

            // Products new columns
            TryExec("ALTER TABLE Products ADD COLUMN PriceYear INTEGER NULL");

            // Estimations new columns
            TryExec("ALTER TABLE Estimations ADD COLUMN MarginPercent REAL NOT NULL DEFAULT 0");
            TryExec("ALTER TABLE Estimations ADD COLUMN Margin2Percent REAL NOT NULL DEFAULT 0");
            TryExec("ALTER TABLE Estimations ADD COLUMN Margin3Percent REAL NOT NULL DEFAULT 0");
            TryExec("ALTER TABLE Estimations ADD COLUMN PPh REAL NOT NULL DEFAULT 0");
            TryExec("ALTER TABLE Estimations ADD COLUMN PPhPercent REAL NOT NULL DEFAULT 0");
            TryExec("ALTER TABLE Estimations ADD COLUMN ContactPhone TEXT NULL");
            TryExec("ALTER TABLE Estimations ADD COLUMN Company TEXT NULL");
            TryExec("ALTER TABLE Estimations ADD COLUMN Address TEXT NULL");
            TryExec("ALTER TABLE Estimations ADD COLUMN ProjectName TEXT NULL");
            TryExec("ALTER TABLE Estimations ADD COLUMN EstimatedOrderDate TEXT NULL");
            TryExec("ALTER TABLE Estimations ADD COLUMN NomorSurat TEXT NULL");

            // Users table
            TryExec(@"CREATE TABLE IF NOT EXISTS Users (
                UserId       INTEGER PRIMARY KEY AUTOINCREMENT,
                Username     TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                FullName     TEXT NOT NULL DEFAULT '',
                Role         TEXT NOT NULL DEFAULT 'Operator',
                IsActive     INTEGER NOT NULL DEFAULT 1,
                CreatedDate  TEXT NOT NULL DEFAULT (datetime('now')),
                LastLoginDate TEXT NULL
            )");

            // Default settings for formal letter export (INSERT OR IGNORE = no-op if already set)
            TryExec("INSERT OR IGNORE INTO AppSettings (SettingKey, SettingValue, LastUpdated) VALUES ('SignerName',    '',                datetime('now'))");
            TryExec("INSERT OR IGNORE INTO AppSettings (SettingKey, SettingValue, LastUpdated) VALUES ('SignerTitle',   'Marketing',       datetime('now'))");
            TryExec("INSERT OR IGNORE INTO AppSettings (SettingKey, SettingValue, LastUpdated) VALUES ('OfferLocation','',                datetime('now'))");
            TryExec("INSERT OR IGNORE INTO AppSettings (SettingKey, SettingValue, LastUpdated) VALUES ('CompanyName',  'PT. Tritunggal Swarna', datetime('now'))");
        }
        catch { /* non-fatal */ }
    }

    private static void SeedDefaultAdmin(PanelCalculator.Data.PanelCalculatorContext context)
    {
        try
        {
            var existing = context.Users.FirstOrDefault(u => u.Username == "admin");
            if (existing == null)
            {
                // First run – create default admin with a fresh BCrypt hash.
                // (Legacy installs that already have a SHA-256 hash will be
                // upgraded silently on next successful login — see LoginForm.)
                context.Users.Add(new User
                {
                    Username     = "admin",
                    PasswordHash = PasswordHasher.Hash("admin"),
                    FullName     = "Administrator",
                    Role         = "Admin",
                    IsActive     = true,
                    CreatedDate  = DateTime.UtcNow
                });
                context.SaveChanges();
            }
            else if (PasswordHasher.Verify("admin123", existing.PasswordHash, out _))
            {
                // Historical migration path: very early installs shipped with
                // password "admin123". Reset to "admin" so the documented
                // default credentials work. Re-hash with BCrypt regardless of
                // the previous storage format.
                existing.PasswordHash = PasswordHasher.Hash("admin");
                context.SaveChanges();
            }
        }
        catch { /* non-fatal */ }
    }

    /// <summary>
    /// Canonical location of the application database. Centralized so
    /// the migrator and the DI container resolve the same path.
    /// </summary>
    private static string GetDbPath()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PanelCalculator",
            "PanelCalculator.db"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        return dbPath;
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        var dbPath = GetDbPath();

        // Connection string carries the SQLCipher passphrase via the
        // standard "Password=" parameter. Microsoft.Data.Sqlite forwards
        // this to sqlite3_key() under the SQLCipher provider.
        var key = MachineKeyProvider.GetKey();
        var connectionString =
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Password   = key,
                Mode       = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            }.ToString();

        // Register DbContext
        services.AddDbContext<PanelCalculatorContext>(options =>
            options.UseSqlite(connectionString),
            ServiceLifetime.Transient
        );

        // Register repositories
        services.AddTransient<IProductRepository, ProductRepository>();
        services.AddTransient<IEstimationRepository, EstimationRepository>();

        // Register services
        services.AddTransient<ICalculationService, CalculationService>();

        // Register forms
        services.AddTransient<LoginForm>();
        services.AddTransient<ShellForm>();
        services.AddTransient<MainForm>();
    }
}
