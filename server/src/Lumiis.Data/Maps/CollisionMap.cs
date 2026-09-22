using System;
using System.IO;
using System.Text;

namespace Lumiis.Data.Maps;

/// <summary>Lädt und validiert eine gebackene Kollisions-Bitmaske (collision.bin, siehe
/// tools/bake-collision.py und CLAUDE.md Abschnitt "Kartenkollision"). Reine Byte-/Bit-Logik, kein
/// Dateizugriff außerhalb von LoadFromFile - World bekommt eine fertige Instanz injiziert, lädt selbst
/// nichts (siehe World-Klassenkommentar).</summary>
public sealed class CollisionMap
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("LCM1");
    private const int HeaderSize = 4 + 4 + 4; // Magic + Breite (int32) + Höhe (int32)

    private readonly bool[] _blocked;

    public int Width { get; }
    public int Height { get; }

    private CollisionMap(int width, int height, bool[] blocked)
    {
        Width = width;
        Height = height;
        _blocked = blocked;
    }

    public static CollisionMap LoadFromFile(string path, int expectedWidth, int expectedHeight)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CollisionMapException($"collision.bin konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromBytes(bytes, expectedWidth, expectedHeight);
    }

    public static CollisionMap LoadFromBytes(byte[] bytes, int expectedWidth, int expectedHeight)
    {
        if (bytes.Length < HeaderSize)
            throw new CollisionMapException($"collision.bin: Datei zu kurz für den Header ({bytes.Length} < {HeaderSize} Bytes).");

        for (int i = 0; i < Magic.Length; i++)
        {
            if (bytes[i] != Magic[i])
                throw new CollisionMapException("collision.bin: falsche Magic-Bytes - keine gültige Kollisionsdatei (mit tools/bake-collision.py neu gebacken?).");
        }

        int width = BitConverter.ToInt32(bytes, 4);
        int height = BitConverter.ToInt32(bytes, 8);

        if (width != expectedWidth || height != expectedHeight)
        {
            throw new CollisionMapException(
                $"collision.bin: Maße ({width}x{height}) stimmen nicht mit GameConstants ({expectedWidth}x{expectedHeight}) überein.");
        }

        int cellCount = width * height;
        int packedBytes = (cellCount + 7) / 8;
        if (bytes.Length < HeaderSize + packedBytes)
        {
            throw new CollisionMapException(
                $"collision.bin: Datei zu kurz für {width}x{height} Zellen ({bytes.Length} < {HeaderSize + packedBytes} Bytes).");
        }

        var blocked = new bool[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            int byteIndex = HeaderSize + i / 8;
            int bitIndex = i % 8;
            blocked[i] = (bytes[byteIndex] & (1 << bitIndex)) != 0;
        }

        return new CollisionMap(width, height, blocked);
    }

    /// <summary>Zellkoordinaten (1 Zelle = 1 Weltenheit = 1 Pixel im Quellbild). Außerhalb der Karte gilt als
    /// blockiert - das betrifft am rechten/unteren Rand auch die exakte Wire-Koordinate GameConstants.MapWidth/
    /// Height selbst (World.SetDestination klemmt Ziele per Math.Clamp inklusiv darauf, das entspricht Zelle
    /// Width/Height, eine mehr als der letzte gültige Index Width-1/Height-1) - bei der echten village-Karte
    /// unsichtbar, weil dort ohnehin die ganze Randzeile/-spalte bemalt blockiert ist (siehe
    /// collision-source.png).</summary>
    public bool IsBlocked(int cellX, int cellY)
    {
        if (cellX < 0 || cellY < 0 || cellX >= Width || cellY >= Height) return true;
        return _blocked[cellY * Width + cellX];
    }

    /// <summary>Wire-Koordinaten (1/100 Welteinheit, siehe CLAUDE.md) - Ganzzahldivision durch 100 auf
    /// Zellkoordinaten, dann wie IsBlocked(cellX, cellY).</summary>
    public bool IsBlockedAtWire(int wireX, int wireY)
    {
        return IsBlocked(wireX / 100, wireY / 100);
    }
}

public sealed class CollisionMapException : Exception
{
    public CollisionMapException(string message) : base(message)
    {
    }
}
