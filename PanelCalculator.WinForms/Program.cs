using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PanelCalculator.Core.Services;
using PanelCalculator.Data;
using PanelCalculator.Data.Repositories;
using PanelCalculator.WinForms.Forms;

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
        }

        // Run main form
        var mainForm = serviceProvider.GetRequiredService<MainForm>();
        Application.Run(mainForm);
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

            void TryAlter(string sql)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                try { cmd.ExecuteNonQuery(); } catch { /* column already exists – ignore */ }
            }

            // EstimationDetails new columns
            TryAlter("ALTER TABLE EstimationDetails ADD COLUMN AdjPercent REAL NOT NULL DEFAULT 0");
            TryAlter("ALTER TABLE EstimationDetails ADD COLUMN Section TEXT NOT NULL DEFAULT 'Material Utama'");

            // Estimations new columns
            TryAlter("ALTER TABLE Estimations ADD COLUMN MarginPercent REAL NOT NULL DEFAULT 0");
            TryAlter("ALTER TABLE Estimations ADD COLUMN PPh REAL NOT NULL DEFAULT 0");
            TryAlter("ALTER TABLE Estimations ADD COLUMN PPhPercent REAL NOT NULL DEFAULT 0");
        }
        catch { /* non-fatal – app still works without new columns on old rows */ }
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
            options.UseSqlite($"Data Source={dbPath};")
        );

        // Register repositories
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IEstimationRepository, EstimationRepository>();

        // Register services
        services.AddScoped<ICalculationService, CalculationService>();

        // Register forms
        services.AddScoped<MainForm>();
    }
}
