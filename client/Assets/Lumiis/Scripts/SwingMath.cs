// Unity kompiliert ohne Nullable-Kontext; diese Datei wird zusätzlich in Lumiis.Tests (Nullable an) eingebunden.
#nullable disable
using System;

namespace Lumiis.Client
{
    /// <summary>Zeitverlauf eines Schwerthiebs, ohne UnityEngine (per dotnet test geprüft). Winkel sind relativ
    /// zur Zielrichtung in Grad (positiv = gegen den Uhrzeigersinn). Ein Hieb hat drei Phasen: Ausholen
    /// (die Klinge zieht zurück), Schlag (schnell durch den Bogen, stark abbremsend) und Nachschwung. Der Client
    /// schickt den Angriff erst in dem Moment an den Server, in dem die Klinge den Winkel des Gegners
    /// überstreicht (<see cref="ContactSeconds"/>) - so entsteht der Treffer sichtbar durch das Schwert.</summary>
    public static class SwingMath
    {
        public const float WindupSeconds = 0.07f;
        public const float StrikeSeconds = 0.12f;
        public const float RecoverSeconds = 0.17f;
        public const float TotalSeconds = WindupSeconds + StrikeSeconds + RecoverSeconds;

        /// <summary>Halber Öffnungswinkel des Hiebs; entspricht der Auto-Target-Bedingung (Blickrichtung ±81°).</summary>
        public const float HalfArcDegrees = 82f;
        private const float WindupPullbackDegrees = 18f;

        /// <summary>Kombo 0 und 2 schlagen von links oben nach rechts unten (im Uhrzeigersinn), Kombo 1 zurück.</summary>
        public static float StartDegrees(int comboIndex) => Sign(comboIndex) * HalfArcDegrees;
        public static float EndDegrees(int comboIndex) => -Sign(comboIndex) * HalfArcDegrees;

        private static float Sign(int comboIndex) => Math.Abs(comboIndex) % 3 == 1 ? -1f : 1f;

        /// <summary>Klingenwinkel relativ zur Zielrichtung zum Zeitpunkt elapsed seit Hiebbeginn.</summary>
        public static float BladeOffsetDegrees(float elapsed, int comboIndex)
        {
            float start = StartDegrees(comboIndex);
            float end = EndDegrees(comboIndex);
            float pull = Sign(comboIndex) * WindupPullbackDegrees;

            if (elapsed <= 0f) return start * 0.55f;
            if (elapsed < WindupSeconds)
            {
                float u = elapsed / WindupSeconds;
                float eased = 1f - (1f - u) * (1f - u);
                return Lerp(start * 0.55f, start + pull, eased);
            }
            if (elapsed < WindupSeconds + StrikeSeconds)
            {
                float u = (elapsed - WindupSeconds) / StrikeSeconds;
                return Lerp(start + pull, end, StrikeProgress(u));
            }
            if (elapsed < TotalSeconds)
            {
                float u = (elapsed - WindupSeconds - StrikeSeconds) / RecoverSeconds;
                float eased = u * u * (3f - 2f * u);
                return Lerp(end, end * 0.45f, eased);
            }
            return end * 0.45f;
        }

        /// <summary>true, solange die Klinge durch den Bogen fährt (Schweif, Nachbilder).</summary>
        public static bool IsStriking(float elapsed) =>
            elapsed >= WindupSeconds && elapsed < WindupSeconds + StrikeSeconds;

        /// <summary>Sekunden nach Hiebbeginn, in denen die Klinge den Gegner berührt. targetOffsetDegrees ist der
        /// Winkel des Gegners relativ zur Zielrichtung; liegt er außerhalb des Bogens, zählt der nächste Rand.</summary>
        public static float ContactSeconds(float targetOffsetDegrees, int comboIndex)
        {
            float start = StartDegrees(comboIndex) + Sign(comboIndex) * WindupPullbackDegrees;
            float end = EndDegrees(comboIndex);
            float clamped = Math.Max(-HalfArcDegrees, Math.Min(HalfArcDegrees, NormalizeDegrees(targetOffsetDegrees)));
            float p = (clamped - start) / (end - start);
            p = Math.Max(0f, Math.Min(1f, p));
            // Umkehrung von StrikeProgress (1 - (1 - u)^3).
            float u = 1f - (float)Math.Pow(1f - p, 1.0 / 3.0);
            return WindupSeconds + StrikeSeconds * u;
        }

        /// <summary>Schlagfortschritt: startet mit voller Geschwindigkeit und bremst zum Ende ab (ease-out cubic).</summary>
        public static float StrikeProgress(float u)
        {
            u = Math.Max(0f, Math.Min(1f, u));
            float inv = 1f - u;
            return 1f - inv * inv * inv;
        }

        /// <summary>Winkel auf (-180, 180].</summary>
        public static float NormalizeDegrees(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees <= -180f) degrees += 360f;
            return degrees;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
