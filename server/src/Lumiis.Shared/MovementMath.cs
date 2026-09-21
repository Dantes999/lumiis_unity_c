namespace Lumiis.Shared
{
    /// <summary>Position einer linearen Bewegung zu einem bestimmten Zeitpunkt (in 1/100 Welteinheit).</summary>
    public struct MovementSample
    {
        public double X;
        public double Y;
        public bool Arrived;
    }

    /// <summary>Reine Bewegungsmathematik, ohne Zustand. Wird vom Server (Vertragstest gegen Entity.Tick) und vom
    /// Unity-Client (Interpolation zwischen EntityMove und EntityStop) genutzt.</summary>
    public static class MovementMath
    {
        /// <summary>Position nach elapsedSeconds einer Bewegung von (startX,startY) nach (targetX,targetY) mit gegebener
        /// Geschwindigkeit (1/100 Einheiten pro Sekunde). Kein Überschießen: bei/nach Ankunft wird exakt targetX/targetY
        /// geliefert. Speed &lt;= 0 oder Distanz 0 bedeutet sofort am Ziel.</summary>
        public static MovementSample Evaluate(int startX, int startY, int targetX, int targetY, int speed, double elapsedSeconds)
        {
            double dx = targetX - startX;
            double dy = targetY - startY;
            double distance = System.Math.Sqrt(dx * dx + dy * dy);

            if (speed <= 0 || distance <= 0.0)
            {
                MovementSample arrivedSample;
                arrivedSample.X = targetX;
                arrivedSample.Y = targetY;
                arrivedSample.Arrived = true;
                return arrivedSample;
            }

            double travelSeconds = distance / speed;

            MovementSample sample;
            if (elapsedSeconds <= 0.0)
            {
                sample.X = startX;
                sample.Y = startY;
                sample.Arrived = false;
            }
            else if (elapsedSeconds >= travelSeconds)
            {
                sample.X = targetX;
                sample.Y = targetY;
                sample.Arrived = true;
            }
            else
            {
                double step = speed * elapsedSeconds;
                sample.X = startX + dx / distance * step;
                sample.Y = startY + dy / distance * step;
                sample.Arrived = false;
            }

            return sample;
        }
    }
}
