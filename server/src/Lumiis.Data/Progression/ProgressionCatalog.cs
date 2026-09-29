using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lumiis.Data.Progression;

/// <summary>Lädt und validiert data/progression.json (M6a, siehe CLAUDE.md Abschnitt "Fortschritt: Level,
/// XP, Werte") - XP-Tabelle (Level 1..MaxLevel-1, wie viel XP zum nächsten Level fehlt) und die
/// Levelunterschieds-Tabelle (Spielerlevel minus Monsterlevel -> Prozent der Basis-XP), analog zu
/// MobCatalog/ClassCatalog.</summary>
public sealed class ProgressionCatalog
{
    private readonly IReadOnlyList<long> _xpToNext;
    private readonly IReadOnlyList<(int MaxDiff, int Percent)> _levelDiffPercent;

    public int MaxLevel { get; }

    private ProgressionCatalog(int maxLevel, IReadOnlyList<long> xpToNext, IReadOnlyList<(int MaxDiff, int Percent)> levelDiffPercent)
    {
        MaxLevel = maxLevel;
        _xpToNext = xpToNext;
        _levelDiffPercent = levelDiffPercent;
    }

    public static ProgressionCatalog LoadFromFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProgressionCatalogException($"progression.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json);
    }

    public static ProgressionCatalog LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        ProgressionCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<ProgressionCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new ProgressionCatalogException($"progression.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new ProgressionCatalogException("progression.json ist leer.");

        if (data.MaxLevel < 2)
            throw new ProgressionCatalogException("progression.json: maxLevel muss mindestens 2 sein.");

        if (data.XpToNext.Count != data.MaxLevel - 1)
        {
            throw new ProgressionCatalogException(
                $"progression.json: xpToNext braucht genau maxLevel-1 Einträge ({data.MaxLevel - 1}), hat aber {data.XpToNext.Count}.");
        }

        foreach (long xp in data.XpToNext)
        {
            if (xp <= 0)
                throw new ProgressionCatalogException($"progression.json: xpToNext-Eintrag {xp} ist <= 0.");
        }

        if (data.LevelDiffPercent.Count == 0)
            throw new ProgressionCatalogException("progression.json: levelDiffPercent braucht mindestens einen Eintrag.");

        var levelDiffPercent = new List<(int MaxDiff, int Percent)>();
        int? previousMaxDiff = null;
        foreach (LevelDiffPercentEntryDto entry in data.LevelDiffPercent)
        {
            if (entry.Percent < 0)
                throw new ProgressionCatalogException($"progression.json: levelDiffPercent-Eintrag mit maxDiff {entry.MaxDiff} hat percent < 0.");

            if (previousMaxDiff.HasValue && entry.MaxDiff <= previousMaxDiff.Value)
            {
                throw new ProgressionCatalogException(
                    "progression.json: levelDiffPercent muss streng aufsteigend nach maxDiff sortiert sein (kein Duplikat/keine Umkehr).");
            }

            levelDiffPercent.Add((entry.MaxDiff, entry.Percent));
            previousMaxDiff = entry.MaxDiff;
        }

        return new ProgressionCatalog(data.MaxLevel, data.XpToNext, levelDiffPercent);
    }

    /// <summary>XP, die vom genannten Level zum nächsten fehlen - 0 ab MaxLevel (kein weiterer Levelaufstieg
    /// möglich, siehe World.AwardXp). level muss zwischen 1 und MaxLevel liegen.</summary>
    public long XpToNextLevel(int level)
    {
        if (level < 1 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Level muss zwischen 1 und {MaxLevel} liegen.");

        return level >= MaxLevel ? 0 : _xpToNext[level - 1];
    }

    /// <summary>Prozent der Basis-XP für den genannten Levelunterschied (Spielerlevel minus Monsterlevel) -
    /// erster Tabelleneintrag, dessen MaxDiff >= diff ist (Tabelle ist aufsteigend sortiert, der letzte
    /// Eintrag fängt jeden noch größeren Unterschied als Katalog-Wert ab).</summary>
    public int PercentForLevelDiff(int diff)
    {
        foreach ((int maxDiff, int percent) in _levelDiffPercent)
        {
            if (diff <= maxDiff) return percent;
        }

        return _levelDiffPercent[^1].Percent;
    }
}

public sealed class ProgressionCatalogException : Exception
{
    public ProgressionCatalogException(string message) : base(message)
    {
    }
}
