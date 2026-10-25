using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumiis.Data.Looks;

namespace Lumiis.Data.Items;

/// <summary>Art eines Items (M5a, M6c) - Armor/Helmet/Weapon/Shield sind Ausrüstungs-Slots mit LookId+Boni,
/// Consumable ist ein Verbrauchsgegenstand mit HealAmount. UpgradeMaterial (Schleifstein) und
/// ProtectionScroll (Bewahrungsrolle) sind reine Aufwertungs-Verbrauchsgüter ohne LookId/Boni und können
/// weder angelegt noch als normaler Heiltrank benutzt werden (siehe World.TryUpgrade).</summary>
public enum ItemType
{
    Armor,
    Helmet,
    Weapon,
    Shield,
    Consumable,
    UpgradeMaterial,
    ProtectionScroll
}

/// <summary>Ein Item aus data/items.json. LookId/AttackBonus/DefenseBonus nur bei Ausrüstungs-ItemTypes
/// aussagekräftig, HealAmount nur bei Consumable.</summary>
public sealed class ItemDefinition
{
    public int Id { get; }
    public string Name { get; }
    public ItemType ItemType { get; }
    public int LookId { get; }
    public int AttackBonus { get; }
    public int DefenseBonus { get; }
    public int HealAmount { get; }

    /// <summary>M6c: Bonus auf Attack/Defense je Aufwertungsstufe (nur Ausrüstung, Default 0). Wird bei
    /// Waffen auf Attack, bei Armor/Helmet/Shield auf Defense addiert (siehe World.RecomputeDerivedStats).</summary>
    public int UpgradeBonusPerLevel { get; }

    /// <summary>M5b: Kaufpreis im Shop - 0 bedeutet "nicht direkt kaufbar" (z. B. Startwaffe), ein NPC-Sortiment
    /// darf ein solches Item nicht listen (siehe NpcCatalog).</summary>
    public long BuyPrice { get; }

    /// <summary>M5b: Verkaufspreis im Shop - 0 bedeutet "nicht verkäuflich" (siehe World.TrySell).</summary>
    public long SellPrice { get; }

    /// <summary>Client-Rendering: statischer Sprite-Dateiname unter Resources/Sprites/Items/ (ohne Endung),
    /// leer = Platzhalter. Wird nur vom Client ausgewertet, der Server validiert lediglich die Stufe.</summary>
    public string SpriteKey { get; }

    /// <summary>Client-Rendering: Pixel-Korrektur des Waffen-Pivots (relativ zum Griff).</summary>
    public float PivotOffsetX { get; }

    public float PivotOffsetY { get; }

    /// <summary>Client-Rendering: Standard-Aufwertungsstufe der Vorlage (0-9).</summary>
    public int UpgradeLevel { get; }

    /// <summary>Client-Rendering: Icon-Schlüssel aus dem Lumiis-Asset-Archiv (optional).</summary>
    public string IconKey { get; }

    public ItemDefinition(int id, string name, ItemType itemType, int lookId, int attackBonus, int defenseBonus, int healAmount,
        long buyPrice, long sellPrice, string spriteKey = "", float pivotOffsetX = 0f, float pivotOffsetY = 0f, int upgradeLevel = 0,
        string iconKey = "", int upgradeBonusPerLevel = 0)
    {
        Id = id;
        Name = name;
        ItemType = itemType;
        LookId = lookId;
        AttackBonus = attackBonus;
        DefenseBonus = defenseBonus;
        HealAmount = healAmount;
        UpgradeBonusPerLevel = upgradeBonusPerLevel;
        BuyPrice = buyPrice;
        SellPrice = sellPrice;
        SpriteKey = spriteKey;
        PivotOffsetX = pivotOffsetX;
        PivotOffsetY = pivotOffsetY;
        UpgradeLevel = upgradeLevel;
        IconKey = iconKey;
    }

    /// <summary>Nur bei Armor/Helmet/Weapon/Shield true - Consumables/Aufwertungsmaterial können nicht
    /// angelegt werden.</summary>
    public bool IsEquipment => ItemType is ItemType.Armor or ItemType.Helmet or ItemType.Weapon or ItemType.Shield;
}

/// <summary>Lädt und validiert data/items.json. Fehler beim Laden bedeuten Abbruch beim Serverstart (analog zu
/// MobCatalog/ClassCatalog). lookCatalog: jedes Ausrüstungs-Item muss einen bestehenden Look-Eintrag im
/// passenden Slot referenzieren (analog zu SpawnCatalog, das MobCatalog/CollisionMap zur Validierung
/// braucht).</summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<int, ItemDefinition> _byId;

    private ItemCatalog(Dictionary<int, ItemDefinition> byId)
    {
        _byId = byId;
    }

    public static ItemCatalog LoadFromFile(string path, LookCatalog lookCatalog)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ItemCatalogException($"items.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json, lookCatalog);
    }

    public static ItemCatalog LoadFromJson(string json, LookCatalog lookCatalog)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        ItemCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<ItemCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new ItemCatalogException($"items.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new ItemCatalogException("items.json ist leer.");

        var byId = new Dictionary<int, ItemDefinition>();
        foreach (ItemEntryDto entry in data.Items)
        {
            if (byId.ContainsKey(entry.Id))
                throw new ItemCatalogException($"items.json: doppelte Id {entry.Id}.");

            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new ItemCatalogException($"items.json: Item {entry.Id} ohne Namen.");

            if (!Enum.TryParse(entry.ItemType, ignoreCase: true, out ItemType itemType))
            {
                throw new ItemCatalogException(
                    $"items.json: Item {entry.Id} ({entry.Name}) hat einen ungültigen itemType '{entry.ItemType}' " +
                    "(erwartet: Armor/Helmet/Weapon/Shield/Consumable/UpgradeMaterial/ProtectionScroll).");
            }

            if (itemType == ItemType.Consumable)
            {
                if (entry.HealAmount <= 0)
                    throw new ItemCatalogException($"items.json: Item {entry.Id} ({entry.Name}) ist Consumable, hat aber healAmount <= 0.");
            }
            else if (itemType is ItemType.UpgradeMaterial or ItemType.ProtectionScroll)
            {
                // M6c: reine Verbrauchsgüter ohne Aussehen/Boni - eine LookId wäre hier ein Datenfehler.
                if (entry.LookId != 0 || entry.AttackBonus != 0 || entry.DefenseBonus != 0 || entry.HealAmount != 0)
                {
                    throw new ItemCatalogException(
                        $"items.json: Item {entry.Id} ({entry.Name}) ist {itemType} und darf keine LookId/Boni/healAmount haben.");
                }
            }
            else
            {
                string slot = itemType.ToString(); // Enum-Namen entsprechen 1:1 den looks.json-Slot-Namen.
                if (!lookCatalog.Exists(slot, entry.LookId))
                {
                    throw new ItemCatalogException(
                        $"items.json: Item {entry.Id} ({entry.Name}) verweist auf unbekannte LookId {entry.LookId} im Slot {slot}.");
                }
            }

            if (entry.UpgradeBonusPerLevel < 0)
                throw new ItemCatalogException($"items.json: Item {entry.Id} ({entry.Name}) hat upgradeBonusPerLevel < 0.");
            if (!IsEquipmentType(itemType) && entry.UpgradeBonusPerLevel != 0)
            {
                throw new ItemCatalogException(
                    $"items.json: Item {entry.Id} ({entry.Name}) ist {itemType} und darf kein upgradeBonusPerLevel haben.");
            }

            if (entry.BuyPrice < 0)
                throw new ItemCatalogException($"items.json: Item {entry.Id} ({entry.Name}) hat buyPrice < 0.");
            if (entry.SellPrice < 0)
                throw new ItemCatalogException($"items.json: Item {entry.Id} ({entry.Name}) hat sellPrice < 0.");

            if (entry.UpgradeLevel < 0 || entry.UpgradeLevel > 9)
                throw new ItemCatalogException($"items.json: Item {entry.Id} ({entry.Name}) hat upgradeLevel {entry.UpgradeLevel} (erlaubt: 0-9).");

            byId[entry.Id] = new ItemDefinition(entry.Id, entry.Name, itemType, entry.LookId, entry.AttackBonus, entry.DefenseBonus, entry.HealAmount,
                entry.BuyPrice, entry.SellPrice, entry.SpriteKey ?? "", entry.PivotOffset?.X ?? 0f, entry.PivotOffset?.Y ?? 0f, entry.UpgradeLevel,
                entry.IconKey ?? "", entry.UpgradeBonusPerLevel);
        }

        if (byId.Count == 0)
            throw new ItemCatalogException("items.json: mindestens ein Item wird benötigt.");

        return new ItemCatalog(byId);
    }

    public bool TryGet(int itemId, out ItemDefinition definition)
    {
        return _byId.TryGetValue(itemId, out definition!);
    }

    private static bool IsEquipmentType(ItemType itemType)
    {
        return itemType is ItemType.Armor or ItemType.Helmet or ItemType.Weapon or ItemType.Shield;
    }
}

public sealed class ItemCatalogException : Exception
{
    public ItemCatalogException(string message) : base(message)
    {
    }
}
