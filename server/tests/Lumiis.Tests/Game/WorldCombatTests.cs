using System;
using System.Collections.Generic;
using Lumiis.Server.Game;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    public class WorldCombatTests
    {
        private const int PlayerMaxHp = 100;
        private const int PlayerAttack = 15;
        private const int PlayerDefense = 5;
        private const int PlayerMeleeRangeWire = 150;
        private const int PlayerAttackCooldownMs = 800;

        private static Entity JoinPlayer(World world, int characterId, int x, int y,
            int maxHp = PlayerMaxHp, int attack = PlayerAttack, int defense = PlayerDefense,
            int meleeRangeWire = PlayerMeleeRangeWire, int attackCooldownMs = PlayerAttackCooldownMs)
        {
            world.TryJoin(characterId, "P" + characterId, x, y, out Entity? entity, out _,
                maxHp, attack, defense, meleeRangeWire, attackCooldownMs);
            return entity!;
        }

        private static Entity SpawnMonster(World world, int x, int y,
            int maxHp = 30, int attack = 5, int defense = 0, int yangMin = 5, int yangMax = 15)
        {
            return world.SpawnMonster(mobId: 1, "Wildhund", x, y, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0,
                maxHp, attack, defense, yangMin, yangMax);
        }

        [Fact]
        public void TryAttack_ValidHit_DealsAttackMinusDefenseDamage()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000, attack: 15);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, maxHp: 30, defense: 5);

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.True(result.Success);
            Assert.Equal(10, result.Damage); // 15 - 5
            Assert.Equal(20, result.TargetHpAfter); // 30 - 10
            Assert.Equal(30, result.TargetMaxHp);
            Assert.False(result.TargetDied);
            Assert.Equal(EntityKind.Monster, result.TargetKind);
        }

        [Fact]
        public void TryAttack_DefenseHigherThanAttack_DealsMinimumOneDamage()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000, attack: 5);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, maxHp: 30, defense: 999);

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.True(result.Success);
            Assert.Equal(1, result.Damage);
        }

        [Fact]
        public void TryAttack_TargetNotFound_Fails()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000);

            AttackResult result = world.TryAttack(player.Id, targetEntityId: 999, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(AttackFailedReason.TargetNotFound, result.Reason);
        }

        [Fact]
        public void TryAttack_OutOfRange_Fails()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000, meleeRangeWire: 150);
            Entity monster = SpawnMonster(world, x: 50000 + 500, y: 50000);

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(AttackFailedReason.OutOfRange, result.Reason);
        }

        [Fact]
        public void TryAttack_WithinRange_Succeeds()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000, meleeRangeWire: 150);
            Entity monster = SpawnMonster(world, x: 50000 + 150, y: 50000); // exakt am Rand, inklusiv

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.True(result.Success);
        }

        [Fact]
        public void TryAttack_OnCooldown_Fails()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000, attackCooldownMs: 800);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, maxHp: 100);

            world.TryAttack(player.Id, monster.Id, nowMs: 0);
            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 500); // < 800ms Cooldown

            Assert.False(result.Success);
            Assert.Equal(AttackFailedReason.OnCooldown, result.Reason);
        }

        [Fact]
        public void TryAttack_AfterCooldownElapsed_Succeeds()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000, attackCooldownMs: 800);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, maxHp: 100);

            world.TryAttack(player.Id, monster.Id, nowMs: 0);
            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 800);

            Assert.True(result.Success);
        }

        [Fact]
        public void TryAttack_DeadTarget_Fails()
        {
            // Monster werden bei Tod sofort entfernt (siehe World.ApplyDamage) - ein "gefundenes, aber
            // totes" Ziel lässt sich über TryAttack nur mit einem Spieler-Ziel erzeugen (ApplyDamage direkt,
            // TryAttack lehnt lebende Spieler-Ziele sonst schon vorher ab - siehe Prüfreihenfolge:
            // "Ziel lebt" kommt vor "Ziel ist kein Spieler").
            var world = new World(maxPlayers: 10);
            Entity attacker = JoinPlayer(world, 1, x: 50000, y: 50000);
            Entity victim = JoinPlayer(world, 2, x: 50000, y: 50000);
            world.ApplyDamage(victim, damage: 9999, nowMs: 0);

            AttackResult result = world.TryAttack(attacker.Id, victim.Id, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(AttackFailedReason.TargetAlreadyDead, result.Reason);
        }

        [Fact]
        public void TryAttack_PlayerTarget_IsRejectedForPvp()
        {
            var world = new World(maxPlayers: 10);
            Entity attacker = JoinPlayer(world, 1, x: 50000, y: 50000);
            Entity otherPlayer = JoinPlayer(world, 2, x: 50000, y: 50000);

            AttackResult result = world.TryAttack(attacker.Id, otherPlayer.Id, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(AttackFailedReason.TargetIsPlayer, result.Reason);
        }

        [Fact]
        public void TryAttack_MonsterDies_YieldsYangInRangeAndIsRemovedWithoutRespawn()
        {
            var world = new World(maxPlayers: 10, collisionMap: null, random: new Random(1));
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000, attack: 999);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, maxHp: 10, yangMin: 5, yangMax: 15);

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.True(result.Success);
            Assert.True(result.TargetDied);
            Assert.Equal(0, result.TargetHpAfter);
            Assert.InRange(result.YangAwarded, 5, 15);
            Assert.False(world.TryGet(monster.Id, out _)); // sofort entfernt, kein Respawn in M4a (TODO M4b)
            Assert.Equal(result.YangAwarded, player.Yang); // dem toetenden Spieler gutgeschrieben
        }

        [Fact]
        public void ApplyDamage_PlayerDies_StaysInWorldWithZeroHpAndSchedulesRespawn()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 10000, y: 10000, maxHp: 100);

            (int hpAfter, bool died, long yangAwarded) = world.ApplyDamage(player, damage: 9999, nowMs: 1000);

            Assert.Equal(0, hpAfter);
            Assert.True(died);
            Assert.Equal(0, yangAwarded); // Spieler zahlen kein Yang aus
            Assert.Equal(0, player.CurrentHp);
            Assert.Equal(1000 + 3000, player.RespawnAtMs); // PlayerRespawnDelayMs, siehe World.cs
            Assert.True(world.TryGet(player.Id, out _)); // kein Despawn
        }

        [Fact]
        public void Tick_DuePlayerRespawn_TeleportsToMapCenterWithFullHp()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 10000, y: 10000, maxHp: 100);
            world.ApplyDamage(player, damage: 9999, nowMs: 1000);

            (_, _, List<Entity> respawned, _) = world.Tick(deltaSeconds: 0.0, nowMs: 1000 + 3000);

            Assert.Contains(player, respawned);
            Assert.Equal(GameConstants.MapWidth / 2, player.IntX);
            Assert.Equal(GameConstants.MapHeight / 2, player.IntY);
            Assert.Equal(100, player.CurrentHp);
            Assert.Equal(0, player.RespawnAtMs);
        }

        [Fact]
        public void Tick_PlayerRespawnNotYetDue_StaysDead()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 10000, y: 10000);
            world.ApplyDamage(player, damage: 9999, nowMs: 1000);

            (_, _, List<Entity> respawned, _) = world.Tick(deltaSeconds: 0.0, nowMs: 1000 + 2999); // 1ms zu frueh

            Assert.Empty(respawned);
            Assert.Equal(0, player.CurrentHp);
        }

        // ---- M4b: Monster-Respawn (siehe docs/tasks/M4b-monster-respawn.md) ----

        [Fact]
        public void SpawnPointMonster_DiesAndRespawnsAfterRespawnSeconds_AtSamePositionWithFullHp()
        {
            var world = new World(maxPlayers: 10, collisionMap: null, random: new Random(1));
            Entity monster = world.SpawnMonster(mobId: 1, "Wildhund", x: 50000, y: 50000, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 999999, wanderIntervalMaxMs: 999999, nowMs: 0,
                maxHp: 30, attack: 5, defense: 0, yangMin: 5, yangMax: 15, spawnPointId: 1, respawnSeconds: 30);

            world.ApplyDamage(monster, damage: 999, nowMs: 0);

            Assert.False(world.TryGet(monster.Id, out _)); // sofort entfernt, wie bisher (M4a)
            Assert.Empty(world.Entities);

            (_, _, _, List<Entity> tooEarly) = world.Tick(0.0, nowMs: 29_999); // 1ms zu frueh
            Assert.Empty(tooEarly);
            Assert.Empty(world.Entities);

            (_, _, _, List<Entity> respawnedMonsters) = world.Tick(0.0, nowMs: 30_000);

            Assert.Single(respawnedMonsters);
            Entity respawned = respawnedMonsters[0];
            Assert.NotEqual(monster.Id, respawned.Id); // neue EntityId, nicht die alte wiederbelebt
            Assert.Equal(1, respawned.MobId);
            Assert.Equal("Wildhund", respawned.Name);
            Assert.Equal(50000, respawned.IntX);
            Assert.Equal(50000, respawned.IntY);
            Assert.Equal(30, respawned.MaxHp);
            Assert.Equal(30, respawned.CurrentHp); // volles Leben
            Assert.Equal(1, respawned.SpawnPointId); // Zyklus bleibt an denselben SpawnPoint gebunden
        }

        [Fact]
        public void DebugSpawnedMonster_Dies_NeverRespawns()
        {
            // Kein spawnPointId uebergeben -> Dev-Tools-Debug-Spawn (siehe Entity.SpawnPointId).
            var world = new World(maxPlayers: 10, collisionMap: null, random: new Random(1));
            Entity monster = world.SpawnMonster(mobId: 1, "Wildhund", x: 50000, y: 50000, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 999999, wanderIntervalMaxMs: 999999, nowMs: 0,
                maxHp: 30, attack: 5, defense: 0, yangMin: 5, yangMax: 15);

            world.ApplyDamage(monster, damage: 999, nowMs: 0);
            Assert.Empty(world.Entities);

            (_, _, _, List<Entity> respawnedMonsters) = world.Tick(0.0, nowMs: 1_000_000); // beliebig viel Zeit
            Assert.Empty(respawnedMonsters);
            Assert.Empty(world.Entities);
        }

        [Fact]
        public void TwoSpawnPoints_RespawnIndependentlyWithTheirOwnRespawnSeconds()
        {
            var world = new World(maxPlayers: 10, collisionMap: null, random: new Random(1));
            Entity monsterA = world.SpawnMonster(mobId: 1, "Wildhund", x: 30000, y: 30000, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 999999, wanderIntervalMaxMs: 999999, nowMs: 0,
                maxHp: 30, attack: 5, defense: 0, yangMin: 5, yangMax: 15, spawnPointId: 1, respawnSeconds: 10);
            Entity monsterB = world.SpawnMonster(mobId: 2, "Waldschrat", x: 60000, y: 60000, speedWire: 200,
                wanderRadiusWire: 300, wanderIntervalMinMs: 999999, wanderIntervalMaxMs: 999999, nowMs: 0,
                maxHp: 60, attack: 8, defense: 3, yangMin: 10, yangMax: 25, spawnPointId: 2, respawnSeconds: 20);

            world.ApplyDamage(monsterA, damage: 999, nowMs: 0);
            world.ApplyDamage(monsterB, damage: 999, nowMs: 0);

            (_, _, _, List<Entity> at10s) = world.Tick(0.0, nowMs: 10_000);
            Assert.Single(at10s);
            Assert.Equal(1, at10s[0].SpawnPointId);

            (_, _, _, List<Entity> at20s) = world.Tick(0.0, nowMs: 20_000);
            Assert.Single(at20s);
            Assert.Equal(2, at20s[0].SpawnPointId);
        }
    }
}
