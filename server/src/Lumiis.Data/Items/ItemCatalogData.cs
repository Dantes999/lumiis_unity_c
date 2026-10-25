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

    /// <summary>M6c: Bonus auf Attack/Defense je Aufwertungsstufe (nur bei Ausrüstung sinnvoll, 0 = kein
    /// Aufwertungsbonus). Wird je nach Itemtyp auf AttackBonus (Waffe) bzw. DefenseBonus (Rest) addiert.</summary>
    public int UpgradeBonusPerLevel { get; set; }

    /// <summary>M5b: Kaufpreis im Shop (0 = nicht im Sortiment kaufbar, siehe NpcCatalog).</summary>
    public long BuyPrice { get; set; }

    /// <summary>M5b: Verkaufspreis im Shop (0 = nicht verkäuflich, siehe World.TrySell).</summary>
    public long SellPrice { get; set; }

    /// <summary>Client-Rendering (Paper Doll): Dateiname des statischen Waffen-PNGs unter
    /// Resources/Sprites/Items/ (ohne Endung). Leer = Client nutzt den generierten Platzhalter.</summary>
    public string SpriteKey { get; set; } = "";

    /// <summary>Client-Rendering: Pixel-Korrektur des Waffen-Pivots relativ zum Griff (x/y).</summary>
    public PivotOffsetDto? PivotOffset { get; set; }

    /// <summary>Client-Rendering: Standard-Aufwertungsstufe der Item-Vorlage (0-9). Die tatsächliche
    /// Instanzstufe kommt später aus dem Inventar; der Wert dient dem Testboard und als Fallback.</summary>
    public int UpgradeLevel { get; set; }

    /// <summary>Client-Rendering: Icon-Schlüssel aus dem Lumiis-Asset-Archiv (optional).</summary>
    public string IconKey { get; set; } = "";
}

internal sealed class PivotOffsetDto
{
    public float X { get; set; }
    public float Y { get; set; }
}

internal sealed class ItemCatalogDto
{
    public List<ItemEntryDto> Items { get; set; } = new();
}
