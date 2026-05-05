namespace PanelCalculator.Core.Models;

public class User
{
    public int    UserId       { get; set; }
    public string Username     { get; set; } = "";
    public string PasswordHash { get; set; } = "";  // SHA-256 hex
    public string FullName     { get; set; } = "";
    public string Role         { get; set; } = "Operator"; // Admin | Operator
    public bool   IsActive     { get; set; } = true;
    public DateTime CreatedDate   { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginDate { get; set; }
}
