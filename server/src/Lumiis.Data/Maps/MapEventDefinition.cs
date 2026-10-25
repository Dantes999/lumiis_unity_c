using System;
using System.Collections.Generic;

namespace Lumiis.Data.Maps;

/// <summary>Mini-Boss-Event einer Karte (data/maps/&lt;karte&gt;/events.json, erzeugt von
/// tools/build-wiki-content.py): der Boss erscheint mit seiner Eskorte an einem der Punkte, sobald die Frist
/// abgelaufen ist, und kommt erst IntervalSeconds nach seinem Tod wieder. Höchstens ein Boss je Event lebt.</summary>
public sealed class MapEventDefinition
{
    public int Id { get; }
    public int BossMobId { get; }
    public IReadOnlyList<(int MobId, int Count)> Escort { get; }
    public int IntervalSeconds { get; }

    /// <summary>Ort für die Ankündigung, z. B. "im Südwesten".</summary>
    public string AreaName { get; }

    /// <summary>Mögliche Erscheinungsorte in Wire-Koordinaten (begehbar, von MapCatalog geprüft).</summary>
    public IReadOnlyList<(int X, int Y)> Points { get; }

    public MapEventDefinition(int id, int bossMobId, IReadOnlyList<(int MobId, int Count)> escort, int intervalSeconds,
        string areaName, IReadOnlyList<(int X, int Y)> points)
    {
        Id = id;
        BossMobId = bossMobId;
        Escort = escort ?? Array.Empty<(int, int)>();
        IntervalSeconds = intervalSeconds;
        AreaName = areaName ?? "";
        Points = points ?? Array.Empty<(int, int)>();
    }
}
