using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using PanelCalculator.Data;
using PanelCalculator.Data.Repositories;
using PanelCalculator.WinForms.Forms;
using System.Security.Cryptography;
using System.Text;

namespace PanelCalculator.WinForms;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

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

        // Show login; if OK, run shell
        var loginForm = serviceProvider.GetRequiredService<LoginForm>();
        loginForm.ShowDialog();      // blocks until closed

        if (loginForm.LoginSuccess)
        {
            var shell = serviceProvider.GetRequiredService<ShellForm>();
            shell.CurrentUser = loginForm.LoggedInUser!;
            Application.Run(shell);
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

            // Estimations new columns
            TryExec("ALTER TABLE Estimations ADD COLUMN MarginPercent REAL NOT NULL DEFAULT 0");
            TryExec("ALTER TABLE Estimations ADD COLUMN PPh REAL NOT NULL DEFAULT 0");
            TryExec("ALTER TABLE Estimations ADD COLUMN PPhPercent REAL NOT NULL DEFAULT 0");
            TryExec("ALTER TABLE Estimations ADD COLUMN ContactPhone TEXT NULL");
            TryExec("ALTER TABLE Estimations ADD COLUMN Company TEXT NULL");
            TryExec("ALTER TABLE Estimations ADD COLUMN Address TEXT NULL");

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
        }
        catch { /* non-fatal */ }
    }

    private static void SeedDefaultAdmin(PanelCalculator.Data.PanelCalculatorContext context)
    {
        try
        {
            var correctHash = HashPassword("admin");
            var oldHash     = HashPassword("admin123");

            var existing = context.Users.FirstOrDefault(u => u.Username == "admin");
            if (existing == null)
            {
                // First run – create default admin
                context.Users.Add(new User
                {
                    Username     = "admin",
                    PasswordHash = correctHash,
                    FullName     = "Administrator",
                    Role         = "Admin",
                    IsActive     = true,
                    CreatedDate  = DateTime.UtcNow
                });
                context.SaveChanges();
            }
            else if (existing.PasswordHash == oldHash)
            {
                // Migrate old "admin123" → "admin"
                existing.PasswordHash = correctHash;
                context.SaveChanges();
            }
        }
        catch { /* non-fatal */ }
    }

    public static string HashPassword(string password)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLower();
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        // Get database path
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PanelCalculator",
            "PanelCalculator.db"
        );

        // Ensure directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // Register DbContext
        services.AddDbContext<PanelCalculatorContext>(options =>
            options.UseSqlite($"Data Source={dbPath};"),
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
