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
    }
}
