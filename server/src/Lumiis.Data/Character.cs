using System;
using System.Collections.Generic;

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

    /// <summary>Ausgerüstete Items (M5a) - Item-IDs aus data/items.json, null = Slot leer. Vor M5a enthielten
    /// diese Spalten LookIds direkt (ArmorId/HelmetId/WeaponId/ShieldId) - beim Umbau auf echte Items wurden
    /// bestehende Werte bewusst auf NULL zurückgesetzt (siehe Migration), weil LookId und ItemId
    /// unterschiedliche Wertebereiche sind und eine automatische Umrechnung nicht sinnvoll möglich ist.</summary>
    public int? EquippedArmorItemId { get; set; }
    public int? EquippedHelmetItemId { get; set; }
    public int? EquippedWeaponItemId { get; set; }
    public int? EquippedShieldItemId { get; set; }

    /// <summary>M6c: Aufwertungsstufe der jeweiligen ausgerüsteten Item-Instanz (0-9). Getrennt von der
    /// ItemId gespeichert, weil Inventar und Ausrüstung denselben Charakter-Slot teilen und ein Item seine
    /// Stufe beim Anlegen/Ablegen mitnehmen muss (siehe CharacterInventoryItem.UpgradeLevel und
    /// World.TryEquip/TryUnequip).</summary>
    public int EquippedArmorUpgradeLevel { get; set; }
    public int EquippedHelmetUpgradeLevel { get; set; }
    public int EquippedWeaponUpgradeLevel { get; set; }
    public int EquippedShieldUpgradeLevel { get; set; }

    /// <summary>Inventar-Slots (M5a) - siehe CharacterInventoryItem.</summary>
    public List<CharacterInventoryItem> InventoryItems { get; set; } = new();

    /// <summary>Gelernte Skillstufen (M6b) - siehe CharacterSkillLevel. Kein Eintrag = Skill nicht gelernt.</summary>
    public List<CharacterSkillLevel> SkillLevels { get; set; } = new();

    /// <summary>Null, solange der Charakter noch nie gespeichert wurde (z. B. direkt nach der Erstellung) -
    /// der Server setzt beim ersten Spawn dann eine Startposition, statt hier eine zu erfinden.</summary>
    public int? PositionX { get; set; }
    public int? PositionY { get; set; }

    /// <summary>Multi-Map (Protocol.Version 16): Id der Karte aus data/maps/index.json. Bestehende Zeilen
    /// bekommen per DB-Default "village" (siehe Migration AddCharacterMapId) - ein unbekannter Wert beim Join
    /// fällt ebenfalls auf die Startkarte zurück (Legacy-Fallback, siehe GameServer.CompleteJoin).</summary>
    public string MapId { get; set; } = "village";

    /// <summary>Kampf-Ertrag (M4a) - Default 0 bei Erstellung, siehe AuthEndpoints.CreateCharacter.</summary>
    public long Yang { get; set; }

    /// <summary>Fortschritt (M6a) - Level/Xp/verteilte Werte/Fertigkeitspunkte, siehe CLAUDE.md Abschnitt
    /// "Fortschritt: Level, XP, Werte". Default-Werte entsprechen einem frischen Level-1-Charakter ohne
    /// verteilte Punkte (siehe AuthEndpoints.CreateCharacter - dort keine Änderung nötig, die DB-Defaults
    /// reichen).</summary>
    public int Level { get; set; } = 1;
    public long Xp { get; set; }
    public int SpentSt { get; set; }
    public int SpentHt { get; set; }
    public int SpentDx { get; set; }
    public int SpentIq { get; set; }
    public int UnspentStatPoints { get; set; }
    public int UnspentSkillPoints { get; set; }

    /// <summary>Null bedeutet "volles Leben/volle SP" (siehe World.RecomputeDerivedStats/CompleteJoin-
    /// Login-Regeln) - ein frischer Charakter hat nie einen gespeicherten Stand. 0 oder darunter bedeutet
    /// "im Tod ausgeloggt", wird beim nächsten Join wiederbelebt (voll + Kartenmitte, Beschluss "Weg 2",
    /// siehe CLAUDE.md).</summary>
    public int? CurrentHp { get; set; }
    public int? CurrentSp { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
