namespace Lumiis.Shared
{
    /// <summary>Leitet die Blickrichtung aus einem Bewegungsvektor ab. Reine Funktion, mit Hysterese gegen
    /// Flackern nahe der Diagonalen (45°-Grenze zwischen Side und Up/Down).</summary>
    public static class FacingMath
    {
        /// <summary>Um wie viel die aktuell nicht aktive Achse dominieren muss, bevor die Achse wechselt.</summary>
        private const double AxisHysteresisRatio = 1.2;

        /// <summary>dx/dy = Bewegungsvektor (z. B. Zielposition minus Startposition). dx = dy = 0 behält previous.
        /// dx &lt; 0 ergibt Side mit FlipX.</summary>
        public static Facing FromDelta(double dx, double dy, Facing previous)
        {
            if (dx == 0.0 && dy == 0.0) return previous;

            double absDx = System.Math.Abs(dx);
            double absDy = System.Math.Abs(dy);
            bool previousWasSide = previous.Direction == FacingDirection.Side;

            bool isSide = previousWasSide
                ? absDx * AxisHysteresisRatio >= absDy
                : absDx > absDy * AxisHysteresisRatio;

            if (isSide)
                return new Facing(FacingDirection.Side, dx < 0.0);

            return new Facing(dy < 0.0 ? FacingDirection.Down : FacingDirection.Up, false);
        }
    }
}
