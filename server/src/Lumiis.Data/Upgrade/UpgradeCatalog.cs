using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumiis.Data.Items;

namespace Lumiis.Data.Upgrade;

/// <summary>Erfolgstabelle und Verbrauchsgüter-IDs für die Ausrüstungs-Aufwertung (M6c), geladen aus
/// data/upgrade.json. Aufwertung ist nur für angelegte Ausrüstung möglich (siehe World.TryUpgrade), die
/// konkrete Instanzstufe lebt auf der Entity/DB (CharacterInventoryItem.UpgradeLevel bzw. die vier
/// EquippedXUpgradeLevel-Spalten), NICHT hier. Alle Zahlen sind bewusst Platzhalter und werden später
/// balanciert (siehe CLAUDE.md "Ausruestungs-Aufwertung").</summary>
public sealed class UpgradeCatalog
{
    /// <summary>Obergrenze aus der MVP-Aufgabenstellung (+0 bis +9). Der Katalog validiert dagegen, damit
    /// eine versehentlich geänderte upgrade.json nicht stillschweigend ein anderes Maximum einführt.</summary>
    public const int SupportedMaxUpgradeLevel = 9;

    private readonly int[] _successChancePercent;

    private UpgradeCatalog(int maxUpgradeLevel, int[] successChancePercent, int whetstoneItemId, int protectionScrollItemId)
    {
        MaxUpgradeLevel = maxUpgradeLevel;
        _successChancePercent = successChancePercent;
        WhetstoneItemId = whetstoneItemId;
        ProtectionScrollItemId = protectionScrollItemId;
    }

    public int MaxUpgradeLevel { get; }
    public int WhetstoneItemId { get; }
    public int ProtectionScrollItemId { get; }

    public IReadOnlyList<int> SuccessChancePercent => _successChancePercent;

    /// <summary>Erfolgschance für den Übergang currentLevel -> currentLevel+1 (0-basiert indexiert, also
    /// Index 0 = +0 -> +1). currentLevel muss in [0, MaxUpgradeLevel-1] liegen.</summary>
    public int GetSuccessChancePercent(int currentLevel)
    {
        if (currentLevel < 0 || currentLevel >= MaxUpgradeLevel)
            throw new ArgumentOutOfRangeException(nameof(currentLevel), currentLevel,
                $"Erfolgschance gibt es nur für Stufen 0..{MaxUpgradeLevel - 1}.");
        return _successChancePercent[currentLevel];
    }

    public static UpgradeCatalog LoadFromFile(string path, ItemCatalog itemCatalog)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpgradeCatalogException($"upgrade.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json, itemCatalog);
    }

    public static UpgradeCatalog LoadFromJson(string json, ItemCatalog itemCatalog)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        UpgradeCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<UpgradeCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new UpgradeCatalogException($"upgrade.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new UpgradeCatalogException("upgrade.json ist leer.");

        if (data.MaxUpgradeLevel != SupportedMaxUpgradeLevel)
        {
            throw new UpgradeCatalogException(
                $"upgrade.json: maxUpgradeLevel {data.MaxUpgradeLevel} wird nicht unterstützt (erwartet: {SupportedMaxUpgradeLevel}).");
        }

        if (data.SuccessChancePercent.Length != data.MaxUpgradeLevel)
        {
            throw new UpgradeCatalogException(
                $"upgrade.json: successChancePercent braucht genau {data.MaxUpgradeLevel} Einträge " +
                $"(+0->+1 bis +{data.MaxUpgradeLevel - 1}->+{data.MaxUpgradeLevel}), gefunden: {data.SuccessChancePercent.Length}.");
        }

        for (int i = 0; i < data.SuccessChancePercent.Length; i++)
        {
            int chance = data.SuccessChancePercent[i];
            if (chance < 0 || chance > 100)
            {
                throw new UpgradeCatalogException(
                    $"upgrade.json: successChancePercent[{i}] = {chance} liegt außerhalb von 0-100.");
            }
        }

        ValidateReferencedItem(data.WhetstoneItemId, ItemType.UpgradeMaterial, "whetstoneItemId", itemCatalog);

        if (data.WhetstoneItemId == data.ProtectionScrollItemId)
            throw new UpgradeCatalogException("upgrade.json: whetstoneItemId und protectionScrollItemId müssen verschieden sein.");

        ValidateReferencedItem(data.ProtectionScrollItemId, ItemType.ProtectionScroll, "protectionScrollItemId", itemCatalog);

        return new UpgradeCatalog(data.MaxUpgradeLevel, data.SuccessChancePercent, data.WhetstoneItemId, data.ProtectionScrollItemId);
    }

    private static void ValidateReferencedItem(int itemId, ItemType expectedType, string fieldName, ItemCatalog itemCatalog)
    {
        if (!itemCatalog.TryGet(itemId, out ItemDefinition item))
        {
            throw new UpgradeCatalogException(
                $"upgrade.json: {fieldName} {itemId} existiert nicht in items.json.");
        }

        if (item.ItemType != expectedType)
        {
            throw new UpgradeCatalogException(
                $"upgrade.json: {fieldName} {itemId} ({item.Name}) hat itemType {item.ItemType}, erwartet: {expectedType}.");
        }
    }
}

public sealed class UpgradeCatalogException : Exception
{
    public UpgradeCatalogException(string message) : base(message)
    {
    }
}
