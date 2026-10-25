// Unity kompiliert ohne Nullable-Kontext; diese Datei wird zusätzlich in Lumiis.Tests (Nullable an) eingebunden.
#nullable disable
using System;

namespace Lumiis.Client
{
    /// <summary>Gleichmäßiger Laufzyklus ohne UnityEngine (per dotnet test geprüft). Vorher wurde der Lauf-Frame
    /// aus |sin(t)| abgeleitet: die Schrittbilder wechselten dadurch in ungleichen Abständen (am Wendepunkt der
    /// Sinuskurve lange, dazwischen kurz) und begannen bei jedem Losgehen an einer zufälligen Stelle - die
    /// Figur "zuckelte". Jetzt läuft eine Phase gleichmäßig hoch: Schritt links, Mitte, Schritt rechts, Mitte.</summary>
    public static class WalkCycle
    {
        /// <summary>Bilder je Sekunde bei Spielertempo (GameConstants.PlayerSpeed = 5 Einheiten/s).</summary>
        public const float FramesPerSecond = 9f;
        public const int FrameCount = 4;

        /// <summary>So lange bleibt die Figur nach einem kurzen Stillstand noch im Laufzustand. Beim Laufen
        /// gegen eine Wand oder wenn ein MoveTo knapp zu spät kommt, meldet die Interpolation für einzelne
        /// Frames "angekommen" - ohne Nachlauf flackerte die Figur dann zwischen Stand und Lauf.</summary>
        public const float StopGraceSeconds = 0.12f;

        /// <summary>Phase nach dt Sekunden Laufen bei speedWire (Wire/s, 0 = Spielertempo).</summary>
        public static float Advance(float phase, float dt, int speedWire)
        {
            float speedFactor = speedWire > 0 ? Math.Min(2f, Math.Max(0.5f, speedWire / 500f)) : 1f;
            float next = phase + Math.Max(0f, dt) * FramesPerSecond * speedFactor;
            return next % FrameCount;
        }

        /// <summary>0 = linker Schritt, 1 = Mitte, 2 = rechter Schritt, 3 = Mitte.</summary>
        public static int Frame(float phase)
        {
            int frame = (int)Math.Floor(phase) % FrameCount;
            return frame < 0 ? frame + FrameCount : frame;
        }

        /// <summary>Wippen 0..1: am tiefsten in der Mitte eines Schrittbilds, am höchsten in der Mitte der
        /// Zwischenbilder - zwei Hebungen pro Zyklus, synchron zu den Beinen.</summary>
        public static float Bob(float phase)
        {
            return 0.5f * (1f - (float)Math.Cos((phase - 0.5f) * Math.PI));
        }

        /// <summary>Spalte im 9-spaltigen Charakter-Sheet: 1 und 2 sind die Schrittbilder, 0 die Mittelstellung.</summary>
        public static int SheetColumn(int frame)
        {
            switch (Frame(frame))
            {
                case 0: return 1;
                case 2: return 2;
                default: return 0;
            }
        }
    }
}
