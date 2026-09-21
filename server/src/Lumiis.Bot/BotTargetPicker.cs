using System;
using Lumiis.Shared;

namespace Lumiis.Bot;

/// <summary>Wählt zufällige MoveTo-Ziele für einen Bot. Reine Funktion (bis auf die Random-Quelle), testbar ohne Netzwerk.</summary>
public static class BotTargetPicker
{
    /// <summary>Berechnet den erlaubten Zielbereich: bei radiusUnits &lt;= 0 die ganze Karte, sonst ein auf die
    /// Kartengrenzen geklemmtes Quadrat von radiusUnits (Welteinheiten, also *100 Serverwerte) um den Spawn-Punkt.</summary>
    public static void GetRange(int spawnX, int spawnY, int radiusUnits, out int minX, out int maxX, out int minY, out int maxY)
    {
        if (radiusUnits <= 0)
        {
            minX = 0;
            maxX = GameConstants.MapWidth;
            minY = 0;
            maxY = GameConstants.MapHeight;
            return;
        }

        int radiusWire = radiusUnits * 100;
        minX = Math.Clamp(spawnX - radiusWire, 0, GameConstants.MapWidth);
        maxX = Math.Clamp(spawnX + radiusWire, 0, GameConstants.MapWidth);
        minY = Math.Clamp(spawnY - radiusWire, 0, GameConstants.MapHeight);
        maxY = Math.Clamp(spawnY + radiusWire, 0, GameConstants.MapHeight);
    }

    /// <summary>Wählt ein zufälliges Ziel innerhalb des von GetRange berechneten Bereichs.</summary>
    public static void PickTarget(int spawnX, int spawnY, int radiusUnits, Random random, out int targetX, out int targetY)
    {
        GetRange(spawnX, spawnY, radiusUnits, out int minX, out int maxX, out int minY, out int maxY);
        targetX = random.Next(minX, maxX + 1);
        targetY = random.Next(minY, maxY + 1);
    }
}
