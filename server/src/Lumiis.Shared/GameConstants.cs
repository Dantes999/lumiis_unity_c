namespace Lumiis.Shared
{
    /// <summary>Spielweite Konstanten (Kartengröße, Bewegung). Koordinaten in 1/100 Welteinheit.</summary>
    public static class GameConstants
    {
        /// <summary>Kartenbreite in 1/100 Welteinheiten (entspricht 1000 Einheiten).</summary>
        public const int MapWidth = 100000;

        /// <summary>Kartenhöhe in 1/100 Welteinheiten (entspricht 1000 Einheiten).</summary>
        public const int MapHeight = 100000;

        /// <summary>Spielergeschwindigkeit in 1/100 Welteinheiten pro Sekunde (entspricht 5 Einheiten/s).</summary>
        public const int PlayerSpeed = 500;

        /// <summary>Sichtbarkeitsradius (AOI) in 1/100 Welteinheiten (entspricht 15 Einheiten) - spätere
        /// Justierung erwartet, siehe CLAUDE.md Abschnitt "Sichtbarkeitsbereich (AOI)".</summary>
        public const int AoiRadiusWire = 1500;

        /// <summary>Anzahl Inventar-Plätze pro Charakter (M5a, siehe CLAUDE.md Abschnitt "Items und
        /// Inventar") - Entity.Inventory (Server) und InventorySyncPacket.Slots (Wire, SlotIndex als byte)
        /// sowie das Debug-Panel (Client) richten sich alle danach.</summary>
        public const int InventorySize = 20;

        /// <summary>Maximale Distanz (1/100 Welteinheiten) zu einem NPC, um dessen Shop zu öffnen/zu nutzen
        /// (M5b, siehe CLAUDE.md "Shop und NPCs") - quadrierte Distanz wie bei AOI/TryAttack, jede
        /// Shop-Aktion prüft das serverseitig erneut.</summary>
        public const int NpcInteractRangeWire = 300;

        /// <summary>Obergrenze für Entity.Yang (M5b) - Monster-Drops, Verkäufe und Debug-Tools kappen ihre
        /// Gutschrift daran (siehe World.CreditYang), damit der Wert nie überläuft oder unbegrenzt wächst.</summary>
        public const long MaxYang = 2_000_000_000;

        /// <summary>Boden-Drops: so lange (ms) darf nur der Spieler, der das Monster getötet hat (oder ein
        /// Mitglied seiner Gruppe), den Drop aufheben - danach jeder. Wie im Vorbild 10 s.</summary>
        public const int GroundItemOwnershipMs = 10_000;

        /// <summary>Boden-Drops verschwinden nach dieser Zeit (ms) von selbst, damit liegengelassene Items die
        /// Welt nicht unbegrenzt füllen. Wie im Vorbild 5 Minuten.</summary>
        public const int GroundItemLifetimeMs = 300_000;

        /// <summary>Reichweite (1/100 Welteinheiten), in der ein Spieler einen Boden-Drop aufheben kann.</summary>
        public const int PickupRangeWire = 300;

        /// <summary>Spielbarkeit: Toleranz auf die Nahkampf-Reichweite, wenn ein SPIELER angreift (Wire). Der
        /// Client sieht laufende Monster mit Netzwerkverzögerung, ohne Spanne meldete der Server bei sichtbar
        /// angrenzenden Zielen "außer Reichweite". Der Client wählt Ziele weiterhin nur innerhalb der exakten
        /// MeleeRangeWire aus, die Toleranz fängt nur den Versatz ab.</summary>
        public const int PlayerMeleeRangeToleranceWire = 50;
    }
}
