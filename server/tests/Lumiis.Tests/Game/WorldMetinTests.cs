using System;
using System.Linq;
using Lumiis.Data.Mobs;
using Lumiis.Server.Game;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>M6d: Lumiis-Stein-Wellen (75/50/25 %) - prueft, dass jede Schwelle genau einmal ausloest,
    /// die konfigurierten Mob-Zahlen erscheinen und ein Tod keine Nachwellen mehr erzeugt.</summary>
    public class WorldMetinTests
    {
        private const string MobsJson = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""Welle A"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 10, ""attack"": 1, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 0, ""meleeRangeWire"": 100, ""attackCooldownMs"": 1000, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 1, ""baseXp"": 1 },
    { ""id"": 2, ""name"": ""Welle B"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 10, ""attack"": 1, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 0, ""meleeRangeWire"": 100, ""attackCooldownMs"": 1000, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 1, ""baseXp"": 1 },
    { ""id"": 3, ""name"": ""Stein"", ""speedWire"": 1, ""wanderRadiusWire"": 1, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 800, ""attack"": 1, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 0, ""meleeRangeWire"": 150, ""attackCooldownMs"": 1000, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 1, ""baseXp"": 1 }
  ]
}";

        private const string MetinsJson = @"{ ""metins"": [ { ""mobId"": 3, ""waves"": [
            { ""hpPercent"": 75, ""mobId"": 1, ""count"": 2 },
            { ""hpPercent"": 50, ""mobId"": 1, ""count"": 3 },
            { ""hpPercent"": 25, ""mobId"": 2, ""count"": 1 } ] } ] }";

        [Fact]
        public void TryAttack_CrossingThresholds_SpawnsEachWaveExactlyOnce()
        {
            MobCatalog mobs = MobCatalog.LoadFromJson(MobsJson);
            MetinCatalog metins = MetinCatalog.LoadFromJson(MetinsJson);
            var world = new World(maxPlayers: 10, random: new Random(1), mobCatalog: mobs, metinCatalog: metins);
            Entity player = JoinPlayer(world);

            MobDefinition stoneDef = GetMob(mobs, 3);
            Entity stone = world.SpawnMonster(stoneDef.Id, stoneDef.Name, 50000, 50000, stoneDef.SpeedWire,
                stoneDef.WanderRadiusWire, stoneDef.WanderIntervalMinMs, stoneDef.WanderIntervalMaxMs, nowMs: 0,
                maxHp: stoneDef.MaxHp, attack: stoneDef.Attack, defense: stoneDef.Defense,
                meleeRangeWire: stoneDef.MeleeRangeWire, attackCooldownMs: stoneDef.AttackCooldownMs,
                level: stoneDef.Level, baseXp: stoneDef.BaseXp);
            player.Teleport(50000, 50000);
            player.Attack = 200;

            world.TryAttack(player.Id, stone.Id, 1000);
            Assert.Equal(600, stone.CurrentHp);
            Assert.Equal(0b001, stone.MetinWavesTriggeredMask);
            Assert.Equal(2, world.Entities.Count(e => e.Kind == EntityKind.Monster && e.Id != stone.Id));

            world.TryAttack(player.Id, stone.Id, 2000);
            Assert.Equal(400, stone.CurrentHp);
            Assert.Equal(0b011, stone.MetinWavesTriggeredMask);
            Assert.Equal(5, world.Entities.Count(e => e.Kind == EntityKind.Monster && e.Id != stone.Id));

            world.TryAttack(player.Id, stone.Id, 3000);
            // 25 %-Welle: 200/800 = 25 %.
            Assert.Equal(200, stone.CurrentHp);
            Assert.Equal(0b111, stone.MetinWavesTriggeredMask);
            Assert.Equal(6, world.Entities.Count(e => e.Kind == EntityKind.Monster && e.Id != stone.Id));

            // Ein weiterer Treffer toetet den Stein - es darf keine weitere Welle geben.
            world.TryAttack(player.Id, stone.Id, 4000);
            Assert.True(stone.IsDead);
            Assert.Equal(6, world.Entities.Count(e => e.Kind == EntityKind.Monster && e.Id != stone.Id));
        }

        private static Entity JoinPlayer(World world)
        {
            bool joined = world.TryJoin(1, "Tester", 50000, 50000, out Entity? player, out _);
            Assert.True(joined);
            return player!;
        }

        private static MobDefinition GetMob(MobCatalog catalog, int id)
        {
            Assert.True(catalog.TryGet(id, out MobDefinition definition));
            return definition;
        }
    }
}
