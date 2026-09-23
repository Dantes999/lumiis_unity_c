using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lumiis.Data.Classes;

/// <summary>Eine Spielerklasse aus data/classes.json (M4a, aktuell nur "Krieger" - siehe CLAUDE.md
/// Alpha-Umfang). Leben/Angriff/Verteidigung/Nahkampfreichweite/Cooldown sind reine Server-Konstanten, keine
/// DB-Spalten (siehe Character - nur Yang wird dort gespeichert).</summary>
public sealed class ClassDefinition
{
    public string Name { get; }
    public int MaxHp { get; }
    public int Attack { get; }
    public int Defense { get; }
    public int MeleeRangeWire { get; }
    public int AttackCooldownMs { get; }

    public ClassDefinition(string name, int maxHp, int attack, int defense, int meleeRangeWire, int attackCooldownMs)
    {
        Name = name;
        MaxHp = maxHp;
        Attack = attack;
        Defense = defense;
        MeleeRangeWire = meleeRangeWire;
        AttackCooldownMs = attackCooldownMs;
    }
}

/// <summary>Lädt und validiert data/classes.json. Fehler beim Laden bedeuten Abbruch beim Serverstart (analog
/// zu MobCatalog/LookCatalog). Schlüssel ist der Klassenname (case-insensitive) statt einer Id - es gibt
/// aktuell keine ClassId-Spalte auf Character, siehe docs/tasks/M4a-combat-core.md Abschnitt 0b.</summary>
public sealed class ClassCatalog
{
    private readonly Dictionary<string, ClassDefinition> _byName;

    private ClassCatalog(Dictionary<string, ClassDefinition> byName)
    {
        _byName = byName;
    }

    public static ClassCatalog LoadFromFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ClassCatalogException($"classes.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json);
    }

    public static ClassCatalog LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        ClassCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<ClassCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new ClassCatalogException($"classes.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new ClassCatalogException("classes.json ist leer.");

        var byName = new Dictionary<string, ClassDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (ClassEntryDto entry in data.Classes)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new ClassCatalogException("classes.json: Klasse ohne Namen.");

            if (byName.ContainsKey(entry.Name))
                throw new ClassCatalogException($"classes.json: doppelter Klassenname {entry.Name}.");

            if (entry.MaxHp <= 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat maxHp <= 0.");

            if (entry.Attack < 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat attack < 0.");

            if (entry.Defense < 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat defense < 0.");

            if (entry.MeleeRangeWire <= 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat meleeRangeWire <= 0.");

            if (entry.AttackCooldownMs <= 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat attackCooldownMs <= 0.");

            byName[entry.Name] = new ClassDefinition(entry.Name, entry.MaxHp, entry.Attack, entry.Defense, entry.MeleeRangeWire, entry.AttackCooldownMs);
        }

        if (byName.Count == 0)
            throw new ClassCatalogException("classes.json: mindestens eine Klasse wird benötigt.");

        return new ClassCatalog(byName);
    }

    public bool TryGet(string name, out ClassDefinition definition)
    {
        return _byName.TryGetValue(name, out definition!);
    }
}

public sealed class ClassCatalogException : Exception
{
    public ClassCatalogException(string message) : base(message)
    {
    }
}
