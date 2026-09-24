using System.Collections.Generic;

namespace Lumiis.Data.Mobs;

// Reine Deserialisierungs-DTOs für data/mobs.json (System.Text.Json, case-insensitive).

internal sealed class MobEntryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SpeedWire { get; set; }
    public int WanderRadiusWire { get; set; }
    public int WanderIntervalMinMs { get; set; }
    public int WanderIntervalMaxMs { get; set; }
    public int MaxHp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int YangMin { get; set; }
    public int YangMax { get; set; }
    public int MeleeRangeWire { get; set; }
    public int AttackCooldownMs { get; set; }
    public bool Aggressive { get; set; }
    public int AggroRangeWire { get; set; }
    public int LeashRangeWire { get; set; }

    /// <summary>M5a: Drops bei Monstertod, siehe MobDrop.</summary>
    public List<MobDropDto> Drops { get; set; } = new();
}

internal sealed class MobDropDto
{
    public int ItemId { get; set; }
    public int ChancePercent { get; set; }
}

internal sealed class MobCatalogDto
{
    public List<MobEntryDto> Mobs { get; set; } = new();
}
