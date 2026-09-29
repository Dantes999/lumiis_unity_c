using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumiis.Shared.Net;

namespace Lumiis.Data.Skills;

/// <summary>Die Werte eines Skills auf einer bestimmten Stufe (1-17, siehe SkillDefinition.Levels) - aus
/// data/skills.json (M6b, siehe CLAUDE.md Abschnitt "Skills"), keine Formel. EffectValue bedeutet je nach
/// SkillDefinition.Type: DamageMultiplierPercent (SingleStrike/AreaAttack), AttackBonusPercent (AttackBuff),
/// CooldownReductionPercent (AttackSpeedBuff) - World.TryUseSkill/RecomputeDerivedStats/
/// EffectiveAttackCooldownMs lesen das jeweils passend.</summary>
public readonly struct SkillLevelDefinition
{
    public int SpCost { get; }
    public int CooldownMs { get; }
    public int EffectValue { get; }

    public SkillLevelDefinition(int spCost, int cooldownMs, int effectValue)
    {
        SpCost = spCost;
        CooldownMs = cooldownMs;
        EffectValue = effectValue;
    }
}

/// <summary>Ein Skill-Typ aus data/skills.json (M6b, aktuell vier Platzhalter-Skills - siehe CLAUDE.md
/// Abschnitt "Skills").</summary>
public sealed class SkillDefinition
{
    public int Id { get; }
    public string Name { get; }
    public SkillType Type { get; }

    /// <summary>Nur bei Type=AreaAttack aussagekräftig (konstant über alle Stufen, siehe
    /// docs/tasks/M6b-skills.md Abschnitt 1).</summary>
    public int RadiusWire { get; }

    /// <summary>Nur bei Type=AttackBuff/AttackSpeedBuff aussagekräftig (konstant über alle Stufen).</summary>
    public int DurationMs { get; }

    /// <summary>Genau 17 Einträge, Index 0 = Stufe 1 (siehe LevelData).</summary>
    public IReadOnlyList<SkillLevelDefinition> Levels { get; }

    public SkillDefinition(int id, string name, SkillType type, int radiusWire, int durationMs, IReadOnlyList<SkillLevelDefinition> levels)
    {
        Id = id;
        Name = name;
        Type = type;
        RadiusWire = radiusWire;
        DurationMs = durationMs;
        Levels = levels;
    }

    /// <summary>level muss zwischen 1 und 17 liegen (siehe Character.SkillLevel-Spaltenbereich).</summary>
    public SkillLevelDefinition LevelData(int level)
    {
        if (level < 1 || level > Levels.Count)
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Level muss zwischen 1 und {Levels.Count} liegen.");
        return Levels[level - 1];
    }
}

/// <summary>Lädt und validiert data/skills.json. Fehler beim Laden bedeuten Abbruch beim Serverstart (analog
/// zu ClassCatalog/MobCatalog/ProgressionCatalog).</summary>
public sealed class SkillCatalog
{
    /// <summary>Charakter.SkillLevel-Spaltenbereich (1-17), siehe docs/tasks/M6b-skills.md.</summary>
    public const int MaxSkillLevel = 17;

    private readonly Dictionary<int, SkillDefinition> _byId;

    private SkillCatalog(Dictionary<int, SkillDefinition> byId)
    {
        _byId = byId;
    }

    public static SkillCatalog LoadFromFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SkillCatalogException($"skills.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json);
    }

    public static SkillCatalog LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        SkillCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<SkillCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new SkillCatalogException($"skills.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new SkillCatalogException("skills.json ist leer.");

        var byId = new Dictionary<int, SkillDefinition>();
        foreach (SkillEntryDto entry in data.Skills)
        {
            if (byId.ContainsKey(entry.Id))
                throw new SkillCatalogException($"skills.json: doppelte Id {entry.Id}.");

            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new SkillCatalogException($"skills.json: Skill {entry.Id} ohne Namen.");

            if (!Enum.TryParse(entry.Type, ignoreCase: true, out SkillType type) || !Enum.IsDefined(typeof(SkillType), type))
                throw new SkillCatalogException($"skills.json: Skill {entry.Id} ({entry.Name}) hat einen unbekannten type \"{entry.Type}\".");

            if (type == SkillType.AreaAttack && entry.RadiusWire <= 0)
                throw new SkillCatalogException($"skills.json: Skill {entry.Id} ({entry.Name}) hat radiusWire <= 0.");

            if ((type == SkillType.AttackBuff || type == SkillType.AttackSpeedBuff) && entry.DurationMs <= 0)
                throw new SkillCatalogException($"skills.json: Skill {entry.Id} ({entry.Name}) hat durationMs <= 0.");

            if (entry.Levels.Count != MaxSkillLevel)
            {
                throw new SkillCatalogException(
                    $"skills.json: Skill {entry.Id} ({entry.Name}) braucht genau {MaxSkillLevel} Stufen-Einträge, hat aber {entry.Levels.Count}.");
            }

            var levels = new List<SkillLevelDefinition>(MaxSkillLevel);
            for (int i = 0; i < entry.Levels.Count; i++)
            {
                SkillLevelEntryDto levelEntry = entry.Levels[i];
                int level = i + 1;

                if (levelEntry.SpCost <= 0)
                    throw new SkillCatalogException($"skills.json: Skill {entry.Id} ({entry.Name}) hat auf Stufe {level} spCost <= 0.");

                if (levelEntry.CooldownMs <= 0)
                    throw new SkillCatalogException($"skills.json: Skill {entry.Id} ({entry.Name}) hat auf Stufe {level} cooldownMs <= 0.");

                int effectValue = type switch
                {
                    SkillType.SingleStrike or SkillType.AreaAttack => levelEntry.DamageMultiplierPercent,
                    SkillType.AttackBuff => levelEntry.AttackBonusPercent,
                    SkillType.AttackSpeedBuff => levelEntry.CooldownReductionPercent,
                    _ => 0
                };
                if (effectValue <= 0)
                    throw new SkillCatalogException($"skills.json: Skill {entry.Id} ({entry.Name}) hat auf Stufe {level} einen Effektwert <= 0.");

                levels.Add(new SkillLevelDefinition(levelEntry.SpCost, levelEntry.CooldownMs, effectValue));
            }

            byId[entry.Id] = new SkillDefinition(entry.Id, entry.Name, type, entry.RadiusWire, entry.DurationMs, levels);
        }

        if (byId.Count == 0)
            throw new SkillCatalogException("skills.json: mindestens ein Skill wird benötigt.");

        return new SkillCatalog(byId);
    }

    public bool TryGet(int skillId, out SkillDefinition definition)
    {
        return _byId.TryGetValue(skillId, out definition!);
    }
}

public sealed class SkillCatalogException : Exception
{
    public SkillCatalogException(string message) : base(message)
    {
    }
}
