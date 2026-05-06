using System.Reflection;

namespace PanelCalculator.Core.Models;

[Obfuscation(Exclude = false, ApplyToMembers = true, Feature = "renaming")]
public class AppSettings
{
    public required string SettingKey { get; set; }

    public string? SettingValue { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
