using System.Collections.Generic;

namespace Lumiis.Data.Items;

// Reine Deserialisierungs-DTOs für data/items.json (System.Text.Json, case-insensitive). ItemType bleibt
// bewusst string (nicht direkt als Enum deserialisiert) - Validierung mit eigener, klarer Fehlermeldung statt
// einer generischen JSON-Konverter-Exception, siehe ItemCatalog.LoadFromJson.

internal sealed class ItemEntryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ItemType { get; set; } = "";
    public int LookId { get; set; }
    public int AttackBonus { get; set; }
    public int DefenseBonus { get; set; }
    public int HealAmount { get; set; }

    /// <summary>M5b: Kaufpreis im Shop (0 = nicht im Sortiment kaufbar, siehe NpcCatalog).</summary>
    public long BuyPrice { get; set; }

    /// <summary>M5b: Verkaufspreis im Shop (0 = nicht verkäuflich, siehe World.TrySell).</summary>
    public long SellPrice { get; set; }
}

internal sealed class ItemCatalogDto
{
    public List<ItemEntryDto> Items { get; set; } = new();
}
