using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumiis.Shared.Net;

namespace Lumiis.Data.Looks;

/// <summary>Lädt und validiert data/looks.json. Fehler beim Laden bedeuten Abbruch beim Serverstart.</summary>
public sealed class LookCatalog
{
    public const string BodySlot = "Body";
    public const string HelmetSlot = "Helmet";

    private readonly LookCatalogDto _data;
    private readonly Dictionary<string, List<int>> _idsBySlot;

    private LookCatalog(LookCatalogDto data)
    {
        _data = data;
        _idsBySlot = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (LookEntryDto entry in data.Looks)
        {
            if (!_idsBySlot.TryGetValue(entry.Slot, out List<int>? ids))
            {
                ids = new List<int>();
                _idsBySlot[entry.Slot] = ids;
            }
            ids.Add(entry.Id);
        }
    }

    public int CanvasPx => _data.Settings.CanvasPx;
    public int Ppu => _data.Settings.Ppu;
    public IReadOnlyList<string> HairColors => _data.HairColors;

    public static LookCatalog LoadFromFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LookCatalogException($"looks.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json);
    }

    public static LookCatalog LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        LookCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<LookCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new LookCatalogException($"looks.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new LookCatalogException("looks.json ist leer.");

        Validate(data);
        return new LookCatalog(data);
    }

    private static void Validate(LookCatalogDto data)
    {
        if (data.HairColors == null || data.HairColors.Count == 0)
            throw new LookCatalogException("looks.json: hairColors darf nicht leer sein.");

        var seenIds = new HashSet<(string Slot, int Id)>();
        bool hasBody = false;

        foreach (LookEntryDto entry in data.Looks)
        {
            if (string.IsNullOrWhiteSpace(entry.Slot))
                throw new LookCatalogException($"looks.json: Look-Eintrag ohne Slot (Id {entry.Id}).");

            (string Slot, int Id) key = (entry.Slot, entry.Id);
            if (!seenIds.Add(key))
                throw new LookCatalogException($"looks.json: doppelte Id {entry.Id} für Slot {entry.Slot}.");

            if (entry.HidesHair && !string.Equals(entry.Slot, HelmetSlot, StringComparison.OrdinalIgnoreCase))
                throw new LookCatalogException($"looks.json: hidesHair ist nur bei Slot {HelmetSlot} erlaubt (Id {entry.Id}, Slot {entry.Slot}).");

            if (string.Equals(entry.Slot, BodySlot, StringComparison.OrdinalIgnoreCase))
                hasBody = true;
        }

        if (!hasBody)
            throw new LookCatalogException($"looks.json: mindestens ein Look mit Slot {BodySlot} wird benötigt.");
    }

    /// <summary>Für die automatische Charaktererstellung in Lumiis.Auth (POST /characters, M2a): erster
    /// gültiger Eintrag je Slot statt Namens-Hash - keine Auswahl in M2a, siehe Aufgabe.</summary>
    public Appearance CreateDefaultAppearance()
    {
        return new Appearance
        {
            BodyId = FirstOrZero(BodySlot),
            HairStyleId = FirstOrZero("Hair"),
            HairColorId = _data.HairColors.Count > 0 ? 1 : 0,
            ArmorId = FirstOrZero("Armor"),
            HelmetId = FirstOrZero(HelmetSlot),
            WeaponId = FirstOrZero("Weapon"),
            ShieldId = FirstOrZero("Shield")
        };
    }

    private int FirstOrZero(string slot)
    {
        List<int>? ids = GetIds(slot);
        return ids != null && ids.Count > 0 ? ids[0] : 0;
    }

    private List<int>? GetIds(string slot)
    {
        _idsBySlot.TryGetValue(slot, out List<int>? ids);
        return ids;
    }
}

public sealed class LookCatalogException : Exception
{
    public LookCatalogException(string message) : base(message)
    {
    }
}
