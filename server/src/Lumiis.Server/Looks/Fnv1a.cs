using System.Text;

namespace Lumiis.Server.Looks;

/// <summary>FNV-1a-Hash (32-bit) über UTF-8-Bytes - deterministisch über Prozessgrenzen hinweg,
/// anders als string.GetHashCode (pro Prozess gesalzen, daher für stabile Zuweisung ungeeignet).</summary>
internal static class Fnv1a
{
    private const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    public static uint Hash(string value)
    {
        unchecked
        {
            uint hash = OffsetBasis;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= Prime;
            }
            return hash;
        }
    }
}
