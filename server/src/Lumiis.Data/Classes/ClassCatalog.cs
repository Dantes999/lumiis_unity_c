using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lumiis.Data.Classes;

/// <summary>Eine Spielerklasse aus data/classes.json (M4a, aktuell nur "Krieger" - siehe CLAUDE.md
/// Alpha-Umfang). MeleeRangeWire/AttackCooldownMs sind direkte Server-Konstanten. Alle anderen Kampf-/
/// Ressourcenwerte (MaxHp/MaxSp/Attack/Defense/Ausweichen) sind seit M6a keine festen Zahlen mehr, sondern
/// werden aus diesen Formelkonstanten plus Level und verteilten Werten berechnet (siehe
/// World.RecomputeDerivedStats, CLAUDE.md Abschnitt "Fortschritt: Level, XP, Werte") - keine DB-Spalten
/// dafür, nur Character.Level/Xp/Spent*/Unspent*/CurrentHp/CurrentSp.</summary>
public sealed class ClassDefinition
{
    public string Name { get; }
    public int MeleeRangeWire { get; }
    public int AttackCooldownMs { get; }

    public int BaseHp { get; }
    public int BaseSp { get; }
    public int BaseAttack { get; }
    public int BaseDefense { get; }

    public int BaseSt { get; }
    public int BaseHt { get; }
    public int BaseDx { get; }
    public int BaseIq { get; }

    public int HpPerHt { get; }
    public int HpPerLevel { get; }
    public int SpPerIq { get; }
    public int SpPerLevel { get; }
    public int AttackPerSt { get; }
    public int HtPerDefense { get; }
    public int DodgeBpPerDx { get; }
    public int DodgeCapBp { get; }

    public int StatPointsPerLevel { get; }
    public int SkillPointsPerLevel { get; }

    public int RegenDelayMs { get; }
    public int RegenIntervalMs { get; }
    public int HpRegenPercent { get; }
    public int SpRegenPercent { get; }

    public ClassDefinition(string name, int meleeRangeWire, int attackCooldownMs,
        int baseHp, int baseSp, int baseAttack, int baseDefense,
        int baseSt, int baseHt, int baseDx, int baseIq,
        int hpPerHt, int hpPerLevel, int spPerIq, int spPerLevel, int attackPerSt, int htPerDefense,
        int dodgeBpPerDx, int dodgeCapBp, int statPointsPerLevel, int skillPointsPerLevel,
        int regenDelayMs, int regenIntervalMs, int hpRegenPercent, int spRegenPercent)
    {
        Name = name;
        MeleeRangeWire = meleeRangeWire;
        AttackCooldownMs = attackCooldownMs;
        BaseHp = baseHp;
        BaseSp = baseSp;
        BaseAttack = baseAttack;
        BaseDefense = baseDefense;
        BaseSt = baseSt;
        BaseHt = baseHt;
        BaseDx = baseDx;
        BaseIq = baseIq;
        HpPerHt = hpPerHt;
        HpPerLevel = hpPerLevel;
        SpPerIq = spPerIq;
        SpPerLevel = spPerLevel;
        AttackPerSt = attackPerSt;
        HtPerDefense = htPerDefense;
        DodgeBpPerDx = dodgeBpPerDx;
        DodgeCapBp = dodgeCapBp;
        StatPointsPerLevel = statPointsPerLevel;
        SkillPointsPerLevel = skillPointsPerLevel;
        RegenDelayMs = regenDelayMs;
        RegenIntervalMs = regenIntervalMs;
        HpRegenPercent = hpRegenPercent;
        SpRegenPercent = spRegenPercent;
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

            if (entry.MeleeRangeWire <= 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat meleeRangeWire <= 0.");

            if (entry.AttackCooldownMs <= 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat attackCooldownMs <= 0.");

            if (entry.BaseHp <= 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat baseHp <= 0.");

            if (entry.BaseSp < 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat baseSp < 0.");

            if (entry.BaseAttack < 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat baseAttack < 0.");

            if (entry.BaseDefense < 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat baseDefense < 0.");

            if (entry.BaseSt <= 0 || entry.BaseHt <= 0 || entry.BaseDx <= 0 || entry.BaseIq <= 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat einen Startwert (baseSt/baseHt/baseDx/baseIq) <= 0.");

            if (entry.HpPerHt < 0 || entry.HpPerLevel < 0 || entry.SpPerIq < 0 || entry.SpPerLevel < 0 || entry.AttackPerSt < 0 || entry.DodgeBpPerDx < 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat einen negativen Formel-Faktor.");

            if (entry.HtPerDefense <= 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat htPerDefense <= 0 (Divisor).");

            if (entry.DodgeCapBp < 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat dodgeCapBp < 0.");

            if (entry.StatPointsPerLevel < 0 || entry.SkillPointsPerLevel < 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat negative Punkte pro Level.");

            if (entry.RegenDelayMs < 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat regenDelayMs < 0.");

            if (entry.RegenIntervalMs <= 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat regenIntervalMs <= 0.");

            if (entry.HpRegenPercent < 0 || entry.SpRegenPercent < 0)
                throw new ClassCatalogException($"classes.json: Klasse {entry.Name} hat negative Regenerationsprozente.");

            byName[entry.Name] = new ClassDefinition(entry.Name, entry.MeleeRangeWire, entry.AttackCooldownMs,
                entry.BaseHp, entry.BaseSp, entry.BaseAttack, entry.BaseDefense,
                entry.BaseSt, entry.BaseHt, entry.BaseDx, entry.BaseIq,
                entry.HpPerHt, entry.HpPerLevel, entry.SpPerIq, entry.SpPerLevel, entry.AttackPerSt, entry.HtPerDefense,
                entry.DodgeBpPerDx, entry.DodgeCapBp, entry.StatPointsPerLevel, entry.SkillPointsPerLevel,
                entry.RegenDelayMs, entry.RegenIntervalMs, entry.HpRegenPercent, entry.SpRegenPercent);
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
