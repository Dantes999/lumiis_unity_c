using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lumiis.Data.Mobs;

/// <summary>Eine Verstaerkungswelle eines Lumiis-Steins: ab hpPercent Rest-HP (oder darunter) werden
/// count Monster vom Typ mobId in der Naehe des Steins erzeugt. Die Wellen sind absteigend sortiert.</summary>
public sealed class MetinWave
{
    public int HpPercent { get; }
    public int MobId { get; }
    public int Count { get; }

    public MetinWave(int hpPercent, int mobId, int count)
    {
        HpPercent = hpPercent;
        MobId = mobId;
        Count = count;
    }
}

/// <summary>Definition eines Lumiis-Steins. Aktuell gilt jeder Mob mit einem Eintrag als Stein; die
/// Wellen werden beim Unterschreiten der HP-Schwellen ausgeloest (siehe World.ResolveHit).</summary>
public sealed class MetinDefinition
{
    public int MobId { get; }
    public IReadOnlyList<MetinWave> Waves { get; }

    public MetinDefinition(int mobId, IReadOnlyList<MetinWave> waves)
    {
        MobId = mobId;
        Waves = waves;
    }
}

/// <summary>Laedt und validiert data/metins.json (analog zu MobCatalog/SpawnCatalog). Der Katalog kennt
/// keine MobCatalog-Referenz; die Wellen-MobIds werden deshalb hier nur auf >0 geprueft. GameServer
/// validiert beim Start zusaetzlich, dass Stein- und Wellen-MobIds existieren.</summary>
public sealed class MetinCatalog
{
    private readonly Dictionary<int, MetinDefinition> _byMobId;

    private MetinCatalog(Dictionary<int, MetinDefinition> byMobId)
    {
        _byMobId = byMobId;
    }

    public static MetinCatalog LoadFromFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MetinCatalogException($"metins.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json);
    }

    public static MetinCatalog LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        MetinCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<MetinCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new MetinCatalogException($"metins.json ist kein gueltiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new MetinCatalogException("metins.json ist leer.");

        var byMobId = new Dictionary<int, MetinDefinition>();
        foreach (MetinEntryDto entry in data.Metins)
        {
            if (entry.MobId <= 0)
                throw new MetinCatalogException("metins.json: mobId muss > 0 sein.");
            if (byMobId.ContainsKey(entry.MobId))
                throw new MetinCatalogException($"metins.json: doppelte mobId {entry.MobId}.");

            var waves = new List<MetinWave>();
            int lastPercent = 101;
            foreach (MetinWaveDto wave in entry.Waves)
            {
                if (wave.HpPercent <= 0 || wave.HpPercent >= 100)
                    throw new MetinCatalogException($"metins.json: Mob {entry.MobId} hat hpPercent ausserhalb 1-99 ({wave.HpPercent}).");
                if (wave.HpPercent >= lastPercent)
                    throw new MetinCatalogException($"metins.json: Mob {entry.MobId} hat nicht absteigende Wellen-Schwellen.");
                if (wave.MobId <= 0)
                    throw new MetinCatalogException($"metins.json: Mob {entry.MobId} hat eine Welle mit mobId <= 0.");
                if (wave.Count <= 0)
                    throw new MetinCatalogException($"metins.json: Mob {entry.MobId} hat eine Welle mit count <= 0.");

                waves.Add(new MetinWave(wave.HpPercent, wave.MobId, wave.Count));
                lastPercent = wave.HpPercent;
            }

            if (waves.Count == 0)
                throw new MetinCatalogException($"metins.json: Mob {entry.MobId} hat keine Wellen.");

            byMobId[entry.MobId] = new MetinDefinition(entry.MobId, waves);
        }

        if (byMobId.Count == 0)
            throw new MetinCatalogException("metins.json: mindestens ein Lumiis-Stein wird benoetigt.");

        return new MetinCatalog(byMobId);
    }

    public bool TryGet(int mobId, out MetinDefinition definition)
    {
        return _byMobId.TryGetValue(mobId, out definition!);
    }

    public IReadOnlyCollection<MetinDefinition> All => _byMobId.Values;

    /// <summary>Validiert alle Stein- und Wellen-MobIds gegen den geladenen MobCatalog. Wird beim
    /// Serverstart aufgerufen (siehe Program.cs), damit ein Tippfehler in metins.json fail-fast auffaellt.</summary>
    public void ValidateAgainst(MobCatalog mobCatalog)
    {
        foreach (MetinDefinition metin in _byMobId.Values)
        {
            if (!mobCatalog.TryGet(metin.MobId, out MobDefinition stone))
                throw new MetinCatalogException($"metins.json: Stein-MobId {metin.MobId} existiert nicht in mobs.json.");

            foreach (MetinWave wave in metin.Waves)
            {
                if (!mobCatalog.TryGet(wave.MobId, out _))
                    throw new MetinCatalogException(
                        $"metins.json: Mob {metin.MobId} ({stone.Name}) verweist in einer Welle auf unbekannte MobId {wave.MobId}.");
            }
        }
    }
}

public sealed class MetinCatalogException : Exception
{
    public MetinCatalogException(string message) : base(message)
    {
    }
}

internal sealed class MetinCatalogDto
{
    public List<MetinEntryDto> Metins { get; set; } = new();
}

internal sealed class MetinEntryDto
{
    public int MobId { get; set; }
    public List<MetinWaveDto> Waves { get; set; } = new();
}

internal sealed class MetinWaveDto
{
    public int HpPercent { get; set; }
    public int MobId { get; set; }
    public int Count { get; set; }
}
