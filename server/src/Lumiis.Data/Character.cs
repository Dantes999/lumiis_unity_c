using System;

namespace Lumiis.Data;

public sealed class Character
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account? Account { get; set; }
    public string Name { get; set; } = "";

    public int BodyId { get; set; }
    public int HairStyleId { get; set; }
    public int HairColorId { get; set; }
    public int ArmorId { get; set; }
    public int HelmetId { get; set; }
    public int WeaponId { get; set; }
    public int ShieldId { get; set; }

    /// <summary>Null, solange der Charakter noch nie gespeichert wurde (z. B. direkt nach der Erstellung) -
    /// der Server setzt beim ersten Spawn dann eine Startposition, statt hier eine zu erfinden.</summary>
    public int? PositionX { get; set; }
    public int? PositionY { get; set; }

    /// <summary>Kampf-Ertrag (M4a) - Default 0 bei Erstellung, siehe AuthEndpoints.CreateCharacter.</summary>
    public long Yang { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
