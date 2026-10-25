using System;
using System.Linq;
using System.Text;
using Lumiis.Data.Maps;
using Lumiis.Data.Mobs;
using Lumiis.Server.Game;
using Lumiis.Shared;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>Karten-Events: Mini-Boss erscheint frühestens nach 30 s mit Eskorte, höchstens einer je Event,
    /// und kommt erst IntervalSeconds nach seinem Tod wieder.</summary>
    public class WorldMapEventTests
    {
        private const string MobsJson = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""Eskorte"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 10, ""attack"": 1, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 0, ""meleeRangeWire"": 100, ""attackCooldownMs"": 1000, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 1, ""baseXp"": 1 },
    { ""id"": 2, ""name"": ""Testboss"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 50, ""attack"": 1, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 0, ""meleeRangeWire"": 100, ""attackCooldownMs"": 1000, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 5, ""baseXp"": 1 }
  ]
}";

        private static CollisionMap EmptyMap()
        {
            int w = GameConstants.MapWidth / 100, h = GameConstants.MapHeight / 100;
            byte[] bytes = new byte[12 + (w * h + 7) / 8];
            Encoding.ASCII.GetBytes("LCM1").CopyTo(bytes, 0);
            BitConverter.GetBytes(w).CopyTo(bytes, 4);
            BitConverter.GetBytes(h).CopyTo(bytes, 8);
            return CollisionMap.LoadFromBytes(bytes, w, h);
        }

        private static World CreateWorld(int intervalSeconds = 600)
        {
            var ev = new MapEventDefinition(7, 2, new[] { (1, 3) }, intervalSeconds, "im Norden", new[] { (60000, 60000) });
            var map = new MapDefinition("m", "Testkarte", EmptyMap(), 50000, 50000, events: new[] { ev });
            return new World(maxPlayers: 10, random: new Random(3), mapDefinition: map,
                mobCatalog: MobCatalog.LoadFromJson(MobsJson));
        }

        [Fact]
        public void Boss_SpawnsWithEscort_NotBefore30s_AndOnlyOnce()
        {
            World world = CreateWorld();
            Assert.Empty(world.RunMapEvents(0));
            Assert.Empty(world.RunMapEvents(29_999));

            // Erste Fälligkeit liegt zwischen 30 s und der halben Intervallzeit (300 s).
            var change = Assert.Single(world.RunMapEvents(300_000));
            Assert.True(change.Spawned);
            Assert.Equal(7, change.EventId);
            Assert.Equal("Testboss", change.BossName);
            Assert.Equal((60000, 60000), (change.X, change.Y));
            Assert.Equal(1, world.Entities.Count(e => e.TypeId == 2));
            Assert.Equal(3, world.Entities.Count(e => e.TypeId == 1));

            Assert.Empty(world.RunMapEvents(400_000));
            var active = Assert.Single(world.ActiveMapEvents());
            Assert.Equal(2, active.MobId);
        }

        [Fact]
        public void BossDeath_IsReported_AndRespawnsAfterInterval()
        {
            World world = CreateWorld(intervalSeconds: 600);
            Assert.Empty(world.RunMapEvents(0)); // Start: Fristen werden ab hier gezählt
            var spawned = Assert.Single(world.RunMapEvents(300_000));
            Entity boss = world.Entities.Single(e => e.Id == spawned.BossEntityId);
            boss.CurrentHp = 0;

            var died = Assert.Single(world.RunMapEvents(310_000));
            Assert.False(died.Spawned);
            Assert.Empty(world.ActiveMapEvents());

            Assert.Empty(world.RunMapEvents(909_999));
            var again = Assert.Single(world.RunMapEvents(910_000));
            Assert.True(again.Spawned);
            Assert.NotEqual(spawned.BossEntityId, again.BossEntityId);
        }

        [Fact]
        public void WithoutEvents_RunMapEvents_IsNoOp()
        {
            var world = new World(maxPlayers: 10);
            Assert.Empty(world.RunMapEvents(1_000_000));
            Assert.Empty(world.ActiveMapEvents());
        }
    }
}
