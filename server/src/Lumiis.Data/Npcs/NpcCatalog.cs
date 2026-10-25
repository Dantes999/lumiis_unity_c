using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumiis.Data.Items;
using Lumiis.Data.Looks;
using Lumiis.Shared.Net;

namespace Lumiis.Data.Npcs;

/// <summary>Ein NPC-Typ aus data/npcs.json. Händler/Schmiede brauchen mindestens ein Item im Sortiment; NPCs ohne
/// Dienst (Wächter, Bürger usw.) dürfen ein leeres Sortiment haben (siehe LoadFromJson).</summary>
public sealed class NpcDefinition
{
    public int Id { get; }
    public string Name { get; }
    public Appearance Appearance { get; }
    public IReadOnlyList<int> ShopItemIds { get; }
    public NpcService Service { get; }

    public NpcDefinition(int id, string name, Appearance appearance, IReadOnlyList<int> shopItemIds, NpcService service = NpcService.None)
    {
        Id = id;
        Name = name;
        Appearance = appearance;
        ShopItemIds = shopItemIds;
        Service = service;
    }
}

/// <summary>Lädt und validiert data/npcs.json (analog zu MobCatalog/ItemCatalog) - jede ShopItemId muss in
/// der übergebenen ItemCatalog existieren UND einen BuyPrice &gt; 0 haben (sonst könnte der Shop ein Item
/// listen, das serverseitig gar nicht kaufbar ist), jede Look-Id der Appearance im jeweiligen Slot von
/// looks.json (lookCatalog, analog zur ItemCatalog-Validierung). Startabbruch mit klarer Meldung inkl.
/// betroffenem NPC/Item bei Verstoß.</summary>
public sealed class NpcCatalog
{
    private readonly Dictionary<int, NpcDefinition> _byId;

    private NpcCatalog(Dictionary<int, NpcDefinition> byId)
    {
        _byId = byId;
    }

    public static NpcCatalog LoadFromFile(string path, ItemCatalog itemCatalog, LookCatalog lookCatalog)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new NpcCatalogException($"npcs.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json, itemCatalog, lookCatalog);
    }

    public static NpcCatalog LoadFromJson(string json, ItemCatalog itemCatalog, LookCatalog lookCatalog)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        NpcCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<NpcCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new NpcCatalogException($"npcs.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new NpcCatalogException("npcs.json ist leer.");

        var byId = new Dictionary<int, NpcDefinition>();
        foreach (NpcEntryDto entry in data.Npcs)
        {
            if (byId.ContainsKey(entry.Id))
                throw new NpcCatalogException($"npcs.json: doppelte Id {entry.Id}.");

            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new NpcCatalogException($"npcs.json: NPC {entry.Id} ohne Namen.");

            Appearance appearance = ValidateAndBuildAppearance(entry, lookCatalog);
            NpcService service = NpcService.None;
            if (!string.IsNullOrWhiteSpace(entry.Service)
                && !Enum.TryParse(entry.Service, ignoreCase: true, out service))
            {
                throw new NpcCatalogException($"npcs.json: NPC {entry.Id} ({entry.Name}) hat einen unbekannten Service '{entry.Service}'.");
            }

            // Ohne Dienst (Wächter, Bürger usw.) darf das Sortiment leer sein; ein Händler/Schmied braucht eins.
            if (entry.ShopItemIds.Count == 0 && service != NpcService.None)
                throw new NpcCatalogException($"npcs.json: NPC {entry.Id} ({entry.Name}) hat ein leeres Sortiment (shopItemIds).");

            foreach (int itemId in entry.ShopItemIds)
            {
                if (!itemCatalog.TryGet(itemId, out ItemDefinition item))
                    throw new NpcCatalogException($"npcs.json: NPC {entry.Id} ({entry.Name}) verweist auf unbekannte ItemId {itemId} im Sortiment.");

                if (item.BuyPrice <= 0)
                {
                    throw new NpcCatalogException(
                        $"npcs.json: NPC {entry.Id} ({entry.Name}) führt Item {itemId} ({item.Name}) im Sortiment, das aber buyPrice <= 0 hat (nicht kaufbar).");
                }
            }

            byId[entry.Id] = new NpcDefinition(entry.Id, entry.Name, appearance, entry.ShopItemIds, service);
        }

        if (byId.Count == 0)
            throw new NpcCatalogException("npcs.json: mindestens ein NPC wird benötigt.");

        return new NpcCatalog(byId);
    }

    /// <summary>0 bedeutet bei Armor/Helmet/Weapon/Shield "keine Ebene" (wie bei Appearance generell, siehe
    /// Lumiis.Shared.Net.Appearance) - wird nicht gegen looks.json geprüft. Body/HairStyle müssen echte,
    /// existierende Look-Ids sein, HairColorId ein gültiger 1-basierter Index in lookCatalog.HairColors.</summary>
    private static Appearance ValidateAndBuildAppearance(NpcEntryDto entry, LookCatalog lookCatalog)
    {
        NpcAppearanceDto dto = entry.Appearance;

        if (!lookCatalog.Exists(LookCatalog.BodySlot, dto.BodyId))
            throw new NpcCatalogException($"npcs.json: NPC {entry.Id} ({entry.Name}) verweist auf unbekannte BodyId {dto.BodyId}.");

        if (!lookCatalog.Exists("Hair", dto.HairStyleId))
            throw new NpcCatalogException($"npcs.json: NPC {entry.Id} ({entry.Name}) verweist auf unbekannte HairStyleId {dto.HairStyleId}.");

        if (dto.HairColorId < 1 || dto.HairColorId > lookCatalog.HairColors.Count)
        {
            throw new NpcCatalogException(
                $"npcs.json: NPC {entry.Id} ({entry.Name}) hat eine ungültige HairColorId {dto.HairColorId} (1-{lookCatalog.HairColors.Count} erwartet).");
        }

        ValidateOptionalLook(entry, "Armor", dto.ArmorId, lookCatalog);
        ValidateOptionalLook(entry, LookCatalog.HelmetSlot, dto.HelmetId, lookCatalog);
        ValidateOptionalLook(entry, "Weapon", dto.WeaponId, lookCatalog);
        ValidateOptionalLook(entry, "Shield", dto.ShieldId, lookCatalog);

        return new Appearance
        {
            BodyId = dto.BodyId,
            HairStyleId = dto.HairStyleId,
            HairColorId = dto.HairColorId,
            ArmorId = dto.ArmorId,
            HelmetId = dto.HelmetId,
            WeaponId = dto.WeaponId,
            ShieldId = dto.ShieldId
        };
    }

    private static void ValidateOptionalLook(NpcEntryDto entry, string slot, int lookId, LookCatalog lookCatalog)
    {
        if (lookId == 0) return; // 0 = keine Ebene, siehe ValidateAndBuildAppearance-Kommentar.
        if (!lookCatalog.Exists(slot, lookId))
            throw new NpcCatalogException($"npcs.json: NPC {entry.Id} ({entry.Name}) verweist auf unbekannte {slot}Id {lookId}.");
    }

    public bool TryGet(int npcId, out NpcDefinition definition)
    {
        return _byId.TryGetValue(npcId, out definition!);
    }
}

public sealed class NpcCatalogException : Exception
{
    public NpcCatalogException(string message) : base(message)
    {
    }
}
