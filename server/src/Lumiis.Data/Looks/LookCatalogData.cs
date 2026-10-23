using System.Collections.Generic;

namespace Lumiis.Data.Looks;

// Reine Deserialisierungs-DTOs für data/looks.json (System.Text.Json, case-insensitive).

internal sealed class LookCatalogSettingsDto
{
    public int CanvasPx { get; set; }
    public int Ppu { get; set; }
}

internal sealed class LookAnchorDto
{
    public int[] Hand { get; set; } = System.Array.Empty<int>();
    public int[] Head { get; set; } = System.Array.Empty<int>();
}

internal sealed class LookAnchorsDto
{
    public LookAnchorDto? Down { get; set; }
    public LookAnchorDto? Up { get; set; }
    public LookAnchorDto? Side { get; set; }
}

internal sealed class LookEntryDto
{
    public int Id { get; set; }
    public string Slot { get; set; } = "";
    public string Name { get; set; } = "";
    public bool HidesHair { get; set; }
    public LookAnchorsDto? Anchors { get; set; }

    /// <summary>Palette-Swapping (Task 2/4): Farbwerte je Rüstungs-Look als Hex-Strings. Optional, damit
    /// ältere/testspezifische Kataloge ohne Farben weiter geladen werden können.</summary>
    public LookColorsDto? Colors { get; set; }
}

internal sealed class LookColorsDto
{
    public string PrimaryColor { get; set; } = "";
    public string SecondaryColor { get; set; } = "";
    public string AccentColor { get; set; } = "";
}

internal sealed class LookCatalogDto
{
    public LookCatalogSettingsDto Settings { get; set; } = new();
    public List<string> HairColors { get; set; } = new();
    public List<LookEntryDto> Looks { get; set; } = new();
}
