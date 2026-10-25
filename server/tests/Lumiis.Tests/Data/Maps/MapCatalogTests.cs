using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lumiis.Data.Items;
using Lumiis.Data.Looks;
using Lumiis.Data.Maps;
using Lumiis.Data.Mobs;
using Lumiis.Data.Npcs;
using Xunit;

namespace Lumiis.Tests.Data.Maps
{
    /// <summary>Multi-Map-Datenmodell (docs/map-import-plan.md): MapCatalog/MapDefinition/PortalDefinition
    /// validieren fail-fast dieselben Fälle, die sonst erst beim Serverstart/Laufzeit auffallen würden.</summary>
    public class MapCatalogTests
    {
        private const int Size = 20;

        /// <summary>20x20-Zellen-Karte (2000x2000 Wire) mit optionalen blockierten Zellen - klein genug für
        /// handgebaute Testfälle, gleiches LCM1-Format wie tools/bake-collision.py.</summary>
        private static CollisionMap BuildMap(params (int X, int Y)[] blockedCells)
        {
            int cellCount = Size * Size;
            byte[] packed = new byte[(cellCount + 7) / 8];
            foreach ((int x, int y) in blockedCells)
            {
                int index = y * Size + x;
                packed[index / 8] |= (byte)(1 << (index % 8));
            }

            byte[] header = new byte[12];
            Encoding.ASCII.GetBytes("LCM1").CopyTo(header, 0);
            BitConverter.GetBytes(Size).CopyTo(header, 4);
            BitConverter.GetBytes(Size).CopyTo(header, 8);

            byte[] bytes = new byte[header.Length + packed.Length];
            header.CopyTo(bytes, 0);
            packed.CopyTo(bytes, header.Length);
            return CollisionMap.LoadFromBytes(bytes, Size, Size);
        }

        private static MapDefinition Map(string id, int startX = 500, int startY = 500,
            IReadOnlyList<PortalDefinition>? portals = null, CollisionMap? collision = null)
        {
            return new MapDefinition(id, id.ToUpperInvariant(), collision ?? BuildMap(), startX, startY, portals);
        }

        private static PortalDefinition Portal(int id, string targetMapId, int targetX = 1500, int targetY = 1500,
            int x1 = 100, int y1 = 100, int x2 = 200, int y2 = 200)
        {
            return new PortalDefinition(id, x1, y1, x2, y2, targetMapId, targetX, targetY);
        }

        [Fact]
        public void TwoMaps_WithReciprocalPortals_AreValid()
        {
            var catalog = new MapCatalog(new[]
            {
                Map("a", portals: new[] { Portal(1, "b") }),
                Map("b", portals: new[] { Portal(1, "a", x1: 100, y1: 100, x2: 200, y2: 200, targetX: 500, targetY: 500) })
            }, "a");

            Assert.Equal("a", catalog.StartMapId);
            Assert.Equal(2, catalog.Maps.Count);
            Assert.True(catalog.TryGet("b", out MapDefinition? b));
            Assert.Equal("B", b!.Name);
            Assert.Single(b.Portals);
        }

        [Fact]
        public void DuplicateMapId_Throws()
        {
            var ex = Assert.Throws<MapCatalogException>(() => new MapCatalog(new[] { Map("a"), Map("a") }, "a"));
            Assert.Contains("doppelte Karten-Id", ex.Message);
        }

        [Fact]
        public void UnknownStartMap_Throws()
        {
            var ex = Assert.Throws<MapCatalogException>(() => new MapCatalog(new[] { Map("a") }, "b"));
            Assert.Contains("Startkarte", ex.Message);
        }

        [Fact]
        public void BlockedStartPosition_Throws()
        {
            var ex = Assert.Throws<MapCatalogException>(() => Map("a", startX: 100, startY: 100, collision: BuildMap((1, 1))));
            Assert.Contains("Startposition", ex.Message);
            Assert.Contains("blockiert", ex.Message);
        }

        [Fact]
        public void StartPositionOutsideMap_Throws()
        {
            var ex = Assert.Throws<MapCatalogException>(() => Map("a", startX: 5000, startY: 500));
            Assert.Contains("außerhalb", ex.Message);
        }

        [Fact]
        public void MissingPortalTarget_Throws()
        {
            var ex = Assert.Throws<MapCatalogException>(() =>
                new MapCatalog(new[] { Map("a", portals: new[] { Portal(1, "does-not-exist") }) }, "a"));
            Assert.Contains("Zielkarte", ex.Message);
            Assert.Contains("does-not-exist", ex.Message);
        }

        [Fact]
        public void BlockedPortalArrival_Throws()
        {
            var target = Map("b", collision: BuildMap((15, 15)));
            var ex = Assert.Throws<MapCatalogException>(() =>
                new MapCatalog(new[] { Map("a", portals: new[] { Portal(1, "b", targetX: 1500, targetY: 1500) }), target }, "a"));
            Assert.Contains("Ankunft", ex.Message);
            Assert.Contains("blockierten Zelle", ex.Message);
        }

        [Fact]
        public void ArrivalInsidePortalOfTargetMap_Throws()
        {
            var target = Map("b", portals: new[] { Portal(1, "a", targetX: 500, targetY: 500) });
            var ex = Assert.Throws<MapCatalogException>(() =>
                new MapCatalog(new[]
                {
                    Map("a", portals: new[] { Portal(1, "b", targetX: 150, targetY: 150) }),
                    target
                }, "a"));
            Assert.Contains("selbst in einem Portal", ex.Message);
        }

        [Fact]
        public void PortalRectangleOutsideMap_Throws()
        {
            var ex = Assert.Throws<MapCatalogException>(() =>
                Map("a", portals: new[] { Portal(1, "a", x1: 100, y1: 100, x2: 5000, y2: 200, targetX: 1500, targetY: 1500) }));
            Assert.Contains("außerhalb der Karte", ex.Message);
        }

        [Fact]
        public void InvertedPortalRectangle_Throws()
        {
            var ex = Assert.Throws<MapCatalogException>(() =>
                Map("a", portals: new[] { Portal(1, "a", x1: 300, y1: 200, x2: 100, y2: 100) }));
            Assert.Contains("invertiert", ex.Message);
        }

        [Fact]
        public void DuplicatePortalId_Throws()
        {
            var ex = Assert.Throws<MapCatalogException>(() =>
                Map("a", portals: new[] { Portal(1, "a", targetX: 1500, targetY: 1500), Portal(1, "a", targetX: 1500, targetY: 1500) }));
            Assert.Contains("doppelte Portal-Id", ex.Message);
        }

        [Fact]
        public void PortalContains_IsInclusive()
        {
            var portal = new PortalDefinition(1, 100, 200, 300, 400, "b", 0, 0);
            Assert.True(portal.Contains(100, 200));
            Assert.True(portal.Contains(300, 400));
            Assert.True(portal.Contains(200, 300));
            Assert.False(portal.Contains(99, 200));
            Assert.False(portal.Contains(301, 400));
        }

        [Fact]
        public void RealMapData_VillageAndGreenfield_LoadAndValidate()
        {
            string repoRoot = FindRepoRoot();
            string dataDir = Path.Combine(repoRoot, "data");
            MobCatalog mobCatalog = MobCatalog.LoadFromFile(Path.Combine(dataDir, "mobs.json"));
            LookCatalog lookCatalog = LookCatalog.LoadFromFile(Path.Combine(dataDir, "looks.json"));
            ItemCatalog itemCatalog = ItemCatalog.LoadFromFile(Path.Combine(dataDir, "items.json"), lookCatalog);
            NpcCatalog npcCatalog = NpcCatalog.LoadFromFile(Path.Combine(dataDir, "npcs.json"), itemCatalog, lookCatalog);

            MapCatalog catalog = MapCatalog.LoadFromDirectory(Path.Combine(dataDir, "maps"), mobCatalog, npcCatalog);

            Assert.Equal("map1", catalog.StartMapId);
            Assert.Equal(3, catalog.Maps.Count);
            Assert.True(catalog.TryGet("map1", out MapDefinition? map1));
            Assert.Equal("Yongan", map1!.Name);
            Assert.NotEmpty(map1.Spawns);
            Assert.NotEmpty(map1.NpcPlacements);
            Assert.False(map1.Collision.IsBlockedAtWire(map1.StartX, map1.StartY));
            // Startbereich bleibt monsterfrei: kein Spawn innerhalb von 20 Weltenheiten (2000 Wire).
            foreach (SpawnPoint spawn in map1.Spawns)
            {
                long dx = spawn.PositionX - map1.StartX;
                long dy = spawn.PositionY - map1.StartY;
                Assert.True(dx * dx + dy * dy > 2000L * 2000L);
            }

            Assert.True(catalog.TryGet("greenfield", out MapDefinition? greenfield));
            Assert.Equal("Gruenes Feld", greenfield!.Name);
            Assert.True(greenfield.Collision.Width >= 1000);

            // Spawns/Portale der echten Dateien sind begehbar und die Ankunft liegt nicht im Quellportal.
            Assert.NotEmpty(greenfield.Spawns);
            Assert.Single(greenfield.Portals);
            Assert.Equal("village", greenfield.Portals[0].TargetMapId);

            MapDefinition village = catalog.Get("village");
            Assert.EndsWith("Dorf", village.Name);
            Assert.Single(village.Portals);
            Assert.False(village.Collision.IsBlockedAtWire(village.Portals[0].X1, village.Portals[0].Y1));
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "data", "maps", "index.json");
                if (File.Exists(candidate)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("Repo-Root mit data/maps/index.json nicht gefunden.");
        }
    }
}
