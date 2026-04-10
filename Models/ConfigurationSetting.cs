// Models/ConfigurationSetting.cs
namespace MortgageLoanAPI.Models;

/// <summary>
/// Represents configurable loan calculation parameters
/// </summary>
public class ConfigurationSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
