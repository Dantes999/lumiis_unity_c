using System.Collections.Generic;

namespace Lumiis.Data.Npcs;

// Reine Deserialisierungs-DTOs für data/npcs.json (System.Text.Json, case-insensitive).

internal sealed class NpcAppearanceDto
{
    public int BodyId { get; set; }
    public int HairStyleId { get; set; }
    public int HairColorId { get; set; }
    public int ArmorId { get; set; }
    public int HelmetId { get; set; }
    public int WeaponId { get; set; }
    public int ShieldId { get; set; }
}

internal sealed class NpcEntryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public NpcAppearanceDto Appearance { get; set; } = new();
    public List<int> ShopItemIds { get; set; } = new();
}

internal sealed class NpcCatalogDto
{
    public List<NpcEntryDto> Npcs { get; set; } = new();
}
