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
    }
}
