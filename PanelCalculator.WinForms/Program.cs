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
        }

        // Run main form
        var mainForm = serviceProvider.GetRequiredService<MainForm>();
        Application.Run(mainForm);
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
