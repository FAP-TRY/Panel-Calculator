using Microsoft.EntityFrameworkCore;
using PanelCalculator.Core.Models;

namespace PanelCalculator.Data;

public class PanelCalculatorContext : DbContext
{
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Estimation> Estimations { get; set; } = null!;
    public DbSet<EstimationDetail> EstimationDetails { get; set; } = null!;
    public DbSet<AppSettings> Settings { get; set; } = null!;

    public PanelCalculatorContext(DbContextOptions<PanelCalculatorContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Product
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.ProductId);
            entity.Property(e => e.ReferenceCode).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ProductName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Price).HasPrecision(18, 2);
            entity.HasIndex(e => e.ReferenceCode).IsUnique();
            entity.HasIndex(e => e.Category);
        });

        // Configure Estimation
        modelBuilder.Entity<Estimation>(entity =>
        {
            entity.HasKey(e => e.EstimationId);
            entity.Property(e => e.EstimationNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ClientName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.SubTotal).HasPrecision(18, 2);
            entity.Property(e => e.ShippingCost).HasPrecision(18, 2);
            entity.Property(e => e.Margin).HasPrecision(18, 2);
            entity.Property(e => e.Tax).HasPrecision(18, 2);
            entity.Property(e => e.TotalPrice).HasPrecision(18, 2);
            entity.HasIndex(e => e.EstimationNumber).IsUnique();
            entity.HasIndex(e => e.CreatedDate);
            entity.HasIndex(e => e.Status);
            entity.HasMany(e => e.Details).WithOne(d => d.Estimation).HasForeignKey(d => d.EstimationId).OnDelete(DeleteBehavior.Cascade);
        });

        // Configure EstimationDetail
        modelBuilder.Entity<EstimationDetail>(entity =>
        {
            entity.HasKey(e => e.DetailId);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.LineTotalPrice).HasPrecision(18, 2);
            entity.HasOne(e => e.Estimation).WithMany(est => est.Details).HasForeignKey(e => e.EstimationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Product).WithMany(p => p.EstimationDetails).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        // Configure AppSettings
        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.HasKey(e => e.SettingKey);
            entity.Property(e => e.SettingKey).IsRequired().HasMaxLength(100);
            entity.Property(e => e.SettingValue).HasMaxLength(1000);
        });

        // Seed default settings
        modelBuilder.Entity<AppSettings>().HasData(
            new AppSettings { SettingKey = "DefaultMarginPercent", SettingValue = "25" },
            new AppSettings { SettingKey = "DefaultTaxPercent", SettingValue = "11" },
            new AppSettings { SettingKey = "CompanyName", SettingValue = "PT Electrical Supplies" },
            new AppSettings { SettingKey = "DefaultShippingCost", SettingValue = "50000" },
            new AppSettings { SettingKey = "CurrencySymbol", SettingValue = "Rp" },
            new AppSettings { SettingKey = "CompanyAddress", SettingValue = "Jakarta, Indonesia" },
            new AppSettings { SettingKey = "CompanyPhone", SettingValue = "+62-21-xxxx-xxxx" }
        );
    }
}
