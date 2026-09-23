using System;
using System.Collections.Generic;

namespace Lumiis.Data;

public sealed class Character
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account? Account { get; set; }
    public string Name { get; set; } = "";

    public int BodyId { get; set; }
    public int HairStyleId { get; set; }
    public int HairColorId { get; set; }

    /// <summary>Ausgerüstete Items (M5a) - Item-IDs aus data/items.json, null = Slot leer. Vor M5a enthielten
    /// diese Spalten LookIds direkt (ArmorId/HelmetId/WeaponId/ShieldId) - beim Umbau auf echte Items wurden
    /// bestehende Werte bewusst auf NULL zurückgesetzt (siehe Migration), weil LookId und ItemId
    /// unterschiedliche Wertebereiche sind und eine automatische Umrechnung nicht sinnvoll möglich ist.</summary>
    public int? EquippedArmorItemId { get; set; }
    public int? EquippedHelmetItemId { get; set; }
    public int? EquippedWeaponItemId { get; set; }
    public int? EquippedShieldItemId { get; set; }

    /// <summary>Inventar-Slots (M5a) - siehe CharacterInventoryItem.</summary>
    public List<CharacterInventoryItem> InventoryItems { get; set; } = new();

    /// <summary>Null, solange der Charakter noch nie gespeichert wurde (z. B. direkt nach der Erstellung) -
    /// der Server setzt beim ersten Spawn dann eine Startposition, statt hier eine zu erfinden.</summary>
    public int? PositionX { get; set; }
    public int? PositionY { get; set; }

    /// <summary>Kampf-Ertrag (M4a) - Default 0 bei Erstellung, siehe AuthEndpoints.CreateCharacter.</summary>
    public long Yang { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
