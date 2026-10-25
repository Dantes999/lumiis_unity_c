namespace Lumiis.Data.Upgrade;

/// <summary>Reines Deserialisierungs-DTO für data/upgrade.json (System.Text.Json, case-insensitive) - siehe
/// UpgradeCatalog.LoadFromJson für die Validierung.</summary>
internal sealed class UpgradeCatalogDto
{
    public int MaxUpgradeLevel { get; set; }
    public int[] SuccessChancePercent { get; set; } = System.Array.Empty<int>();
    public int WhetstoneItemId { get; set; }
    public int ProtectionScrollItemId { get; set; }
}
