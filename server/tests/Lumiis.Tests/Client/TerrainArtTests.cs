using System;
using System.IO;
using System.Linq;
using Lumiis.Client;
using Xunit;

namespace Lumiis.Tests.Client
{
    /// <summary>TerrainArt (Client-Bodenzeichnung, UnityEngine-frei) gegen die echten terrain.bin-Dateien.</summary>
    public class TerrainArtTests
    {
        private static string ResolveDataDir()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "mobs.json"))) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("data/mobs.json nicht gefunden.");
        }

        private static TerrainMap Load(string mapId)
        {
            return TerrainMap.Parse(File.ReadAllBytes(Path.Combine(ResolveDataDir(), "maps", mapId, "terrain.bin")));
        }

        [Fact]
        public void Parse_RejectsWrongHeaderAndLength()
        {
            Assert.Throws<InvalidDataException>(() => TerrainMap.Parse(new byte[] { 1, 2, 3 }));
            byte[] data = { (byte)'L', (byte)'T', (byte)'R', (byte)'1', 2, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0 };
            Assert.Throws<InvalidDataException>(() => TerrainMap.Parse(data));
        }

        [Theory]
        [InlineData("map1", 323, 292, 11)]
        [InlineData("map2", 229, 317, 10)]
        public void PaintChunk_RealMaps_IsOpaqueAndDeterministic(string mapId, int vx, int vy, int r)
        {
            TerrainMap map = Load(mapId);
            var art = new TerrainArt(map, vx, vy, r);
            int cx = vx / TerrainArt.ChunkTiles, cy = vy / TerrainArt.ChunkTiles;

            uint[] a = art.PaintChunk(cx, cy, true);
            uint[] b = art.PaintChunk(cx, cy, true);

            Assert.Equal(TerrainArt.ChunkPixels * TerrainArt.ChunkPixels, a.Length);
            Assert.All(a, p => Assert.Equal(0xffu, p >> 24));
            Assert.Equal(a, b);
            Assert.True(a.Distinct().Count() > 8, "Dorfplatz sollte nicht einfarbig sein");

            // Randchunk (Berge) läuft ebenfalls ohne Fehler
            uint[] corner = art.PaintChunk(0, 0, false);
            Assert.All(corner, p => Assert.Equal(0xffu, p >> 24));
        }

        [Fact]
        public void Props_AllKindsPaintWithFootpointInsideImage()
        {
            TerrainMap map = Load("map1");
            var art = new TerrainArt(map);
            var extra = new[]
            {
                new TerrainProp { Type = "house", Kind = "small", Tx = 10, Ty = 10, W = 4, H = 6 },
                new TerrainProp { Type = "house", Kind = "hall", Tx = 10, Ty = 10, W = 7, H = 5 },
                new TerrainProp { Type = "monument", Tx = 1, Ty = 1 },
                new TerrainProp { Type = "lantern", Tx = 1, Ty = 1 },
                new TerrainProp { Type = "well", Tx = 1, Ty = 1, W = 2, H = 2 },
            };
            var props = art.CollectProps(extra);
            Assert.True(props.Count(p => p.Type == "tree") > 500);
            foreach (TerrainProp p in props.GroupBy(TerrainArt.ImageKey).Select(g => g.First()))
            {
                PropImage? img = TerrainArt.PaintProp(p);
                Assert.NotNull(img);
                Assert.InRange(img!.Ox, 0, img.Width);
                Assert.InRange(img.Oy, 0, img.Height);
                Assert.Contains(img.Rgba.Where((_, i) => i % 4 == 3), alpha => alpha == 255);
            }
        }
    }
}
