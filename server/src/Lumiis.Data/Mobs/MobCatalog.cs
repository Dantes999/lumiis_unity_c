using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lumiis.Data.Mobs;

/// <summary>Ein möglicher Drop beim Tod eines Mob-Typs (M5a) - ItemId referenziert data/items.json (kein
/// Katalog-Querverweis beim Laden, siehe MobCatalog.LoadFromJson), ChancePercent 0-100, jeder Eintrag
/// unabhängig gewürfelt (siehe World.TryAttack).</summary>
public readonly struct MobDrop
{
    public int ItemId { get; }
    public int ChancePercent { get; }

    public MobDrop(int itemId, int chancePercent)
    {
        ItemId = itemId;
        ChancePercent = chancePercent;
    }
}

/// <summary>Ein Mob-Typ aus data/mobs.json (Platzhalterdaten, siehe CLAUDE.md Abschnitt "Monster").</summary>
public sealed class MobDefinition
{
    public int Id { get; }
    public string Name { get; }
    public int SpeedWire { get; }
    public int WanderRadiusWire { get; }
    public int WanderIntervalMinMs { get; }
    public int WanderIntervalMaxMs { get; }
    public int MaxHp { get; }
    public int Attack { get; }
    public int Defense { get; }

    /// <summary>Yang-Spanne, die beim Tod dieses Mob-Typs ausgezahlt wird (M4a, siehe World.TryAttack).</summary>
    public int YangMin { get; }
    public int YangMax { get; }

    /// <summary>M4c: Kampfwerte für Monster als Angreifer, analog zum Spieler-Krieger aus classes.json.</summary>
    public int MeleeRangeWire { get; }
    public int AttackCooldownMs { get; }

    /// <summary>M4c: greift dieser Mob-Typ von sich aus an, sobald ein Spieler in AggroRangeWire kommt?
    /// AggroRangeWire ist nur bei Aggressive=true aussagekräftig. Passive Mobs (Aggressive=false) erfassen ein
    /// Ziel ausschließlich reaktiv nach einem Treffer (siehe World.TryAttack).</summary>
    public bool Aggressive { get; }
    public int AggroRangeWire { get; }

    /// <summary>M4c: Distanz vom Wander-Anker, ab der ein verfolgtes Ziel aufgegeben wird (siehe
    /// World-Tick-Monster-KI).</summary>
    public int LeashRangeWire { get; }

    /// <summary>M5a: mögliche Drops beim Tod dieses Mob-Typs (siehe World.TryAttack). Leer, wenn keine
    /// definiert sind.</summary>
    public IReadOnlyList<MobDrop> Drops { get; }

    public MobDefinition(int id, string name, int speedWire, int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs,
        int maxHp, int attack, int defense, int yangMin, int yangMax,
        int meleeRangeWire, int attackCooldownMs, bool aggressive, int aggroRangeWire, int leashRangeWire,
        IReadOnlyList<MobDrop> drops)
    {
        Id = id;
        Name = name;
        SpeedWire = speedWire;
        WanderRadiusWire = wanderRadiusWire;
        WanderIntervalMinMs = wanderIntervalMinMs;
        WanderIntervalMaxMs = wanderIntervalMaxMs;
        MaxHp = maxHp;
        Attack = attack;
        Defense = defense;
        YangMin = yangMin;
        YangMax = yangMax;
        MeleeRangeWire = meleeRangeWire;
        AttackCooldownMs = attackCooldownMs;
        Aggressive = aggressive;
        AggroRangeWire = aggroRangeWire;
        LeashRangeWire = leashRangeWire;
        Drops = drops;
    }
}

/// <summary>Lädt und validiert data/mobs.json. Fehler beim Laden bedeuten Abbruch beim Serverstart (analog zu
/// LookCatalog).</summary>
public sealed class MobCatalog
{
    private readonly Dictionary<int, MobDefinition> _byId;

    private MobCatalog(Dictionary<int, MobDefinition> byId)
    {
        _byId = byId;
    }

    public static MobCatalog LoadFromFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MobCatalogException($"mobs.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json);
    }

    public static MobCatalog LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        MobCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<MobCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new MobCatalogException($"mobs.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new MobCatalogException("mobs.json ist leer.");

        var byId = new Dictionary<int, MobDefinition>();
        foreach (MobEntryDto entry in data.Mobs)
        {
            if (byId.ContainsKey(entry.Id))
                throw new MobCatalogException($"mobs.json: doppelte Id {entry.Id}.");

            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ohne Name.");

            if (entry.SpeedWire <= 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat speedWire <= 0.");

            if (entry.WanderRadiusWire <= 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat wanderRadiusWire <= 0.");

            if (entry.WanderIntervalMinMs <= 0 || entry.WanderIntervalMaxMs < entry.WanderIntervalMinMs)
            {
                throw new MobCatalogException(
                    $"mobs.json: Mob {entry.Id} ({entry.Name}) hat ein ungültiges Wander-Intervall (min {entry.WanderIntervalMinMs}, max {entry.WanderIntervalMaxMs}).");
            }

            if (entry.MaxHp <= 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat maxHp <= 0.");

            if (entry.Attack < 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat attack < 0.");

            if (entry.Defense < 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat defense < 0.");

            if (entry.YangMin < 0 || entry.YangMax < entry.YangMin)
            {
                throw new MobCatalogException(
                    $"mobs.json: Mob {entry.Id} ({entry.Name}) hat eine ungültige Yang-Spanne (min {entry.YangMin}, max {entry.YangMax}).");
            }

            if (entry.MeleeRangeWire <= 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat meleeRangeWire <= 0.");

            if (entry.AttackCooldownMs <= 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat attackCooldownMs <= 0.");

            if (entry.AggroRangeWire < 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat aggroRangeWire < 0.");

            if (entry.LeashRangeWire <= 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat leashRangeWire <= 0.");

            var drops = new List<MobDrop>();
            foreach (MobDropDto dropEntry in entry.Drops)
            {
                if (dropEntry.ItemId <= 0)
                    throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat einen Drop mit itemId <= 0.");

                if (dropEntry.ChancePercent < 0 || dropEntry.ChancePercent > 100)
                {
                    throw new MobCatalogException(
                        $"mobs.json: Mob {entry.Id} ({entry.Name}) hat einen Drop (ItemId {dropEntry.ItemId}) mit chancePercent außerhalb 0-100 ({dropEntry.ChancePercent}).");
                }

                drops.Add(new MobDrop(dropEntry.ItemId, dropEntry.ChancePercent));
            }

            byId[entry.Id] = new MobDefinition(entry.Id, entry.Name, entry.SpeedWire, entry.WanderRadiusWire, entry.WanderIntervalMinMs, entry.WanderIntervalMaxMs,
                entry.MaxHp, entry.Attack, entry.Defense, entry.YangMin, entry.YangMax,
                entry.MeleeRangeWire, entry.AttackCooldownMs, entry.Aggressive, entry.AggroRangeWire, entry.LeashRangeWire, drops);
        }

        if (byId.Count == 0)
            throw new MobCatalogException("mobs.json: mindestens ein Mob-Typ wird benötigt.");

        return new MobCatalog(byId);
    }

    public bool TryGet(int mobId, out MobDefinition definition)
    {
        return _byId.TryGetValue(mobId, out definition!);
    }
}

public sealed class MobCatalogException : Exception
{
    public MobCatalogException(string message) : base(message)
    {
    }
}
