namespace PanelCalculator.Core.Models;

public class AppSettings
{
    public required string SettingKey { get; set; }

    public string? SettingValue { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
