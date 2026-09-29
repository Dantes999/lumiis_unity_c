using System;
using System.Collections.Generic;
using Lumiis.Server.Game;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>M4c: Zielerfassung/Verfolgung/Leine/Monster-als-Angreifer (siehe docs/tasks/M4c-monster-aggro.md
    /// Abschnitte 3/4/5) - kein Netzwerkbezug, wie WorldCombatTests.</summary>
    public class WorldMonsterAiTests
    {
        private static Entity JoinPlayer(World world, int characterId, int x, int y, int maxHp = 100)
        {
            world.TryJoin(characterId, "P" + characterId, x, y, out Entity? entity, out _, maxHp: maxHp);
            return entity!;
        }

        private static Entity SpawnMonster(World world, int x, int y, bool aggressive, int aggroRangeWire, int leashRangeWire,
            int meleeRangeWire = 130, int attackCooldownMs = 700, int attack = 5, int maxHp = 30, int defense = 0)
        {
            return world.SpawnMonster(mobId: 1, "Wildhund", x, y, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 999999, wanderIntervalMaxMs: 999999, nowMs: 0,
                maxHp: maxHp, attack: attack, defense: defense, yangMin: 5, yangMax: 15,
                meleeRangeWire: meleeRangeWire, attackCooldownMs: attackCooldownMs,
                aggressive: aggressive, aggroRangeWire: aggroRangeWire, leashRangeWire: leashRangeWire);
        }

        [Fact]
        public void AggressiveMonster_PlayerWithinAggroRange_AcquiresTarget()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000);
            Entity monster = SpawnMonster(world, x: 50300, y: 50000, aggressive: true, aggroRangeWire: 400, leashRangeWire: 700);

            world.Tick(0.0, nowMs: 0);

            Assert.Equal(player.Id, monster.TargetEntityId);
        }

        [Fact]
        public void AggressiveMonster_PlayerOutsideAggroRange_IgnoresPlayer()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000);
            Entity monster = SpawnMonster(world, x: 50500, y: 50000, aggressive: true, aggroRangeWire: 400, leashRangeWire: 700);

            world.Tick(0.0, nowMs: 0);

            Assert.Null(monster.TargetEntityId);
        }

        [Fact]
        public void PassiveMonster_PlayerAdjacent_DoesNotAttackOnItsOwn()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000);
            SpawnMonster(world, x: 50000, y: 50000, aggressive: false, aggroRangeWire: 0, leashRangeWire: 600);

            world.Tick(0.0, nowMs: 0);
            world.Tick(0.0, nowMs: 40);

            Assert.Equal(100, player.CurrentHp); // unveraendert - kein von sich aus ausgeloester Angriff
        }

        [Fact]
        public void PassiveMonster_HitByPlayer_AcquiresAttackerAsTarget()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000);
            Entity monster = SpawnMonster(world, x: 50130, y: 50000, aggressive: false, aggroRangeWire: 0, leashRangeWire: 600);

            world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.Equal(player.Id, monster.TargetEntityId);
        }

        [Fact]
        public void MonsterWithTargetInMeleeRange_AttacksOnCooldownRhythm()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000, maxHp: 1000);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, aggressive: true, aggroRangeWire: 400, leashRangeWire: 700,
                attackCooldownMs: 700, attack: 5);

            world.Tick(0.0, nowMs: 0); // Ziel erfassen (Distanz 0, direkt im Nahkampf)
            Assert.Equal(player.Id, monster.TargetEntityId);

            (_, _, _, _, List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> firstHit, _) = world.Tick(0.0, nowMs: 40);
            Assert.Single(firstHit);
            Assert.Equal(1000 - 5, player.CurrentHp);

            (_, _, _, _, List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> tooEarly, _) = world.Tick(0.0, nowMs: 40 + 500); // < 700ms Cooldown
            Assert.Empty(tooEarly);

            (_, _, _, _, List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> secondHit, _) = world.Tick(0.0, nowMs: 40 + 700);
            Assert.Single(secondHit);
            Assert.Equal(1000 - 10, player.CurrentHp);
        }

        [Fact]
        public void MonsterWithTargetOutsideMeleeButInsideLeash_MovesTowardPlayer()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50300, y: 50000);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, aggressive: true, aggroRangeWire: 400, leashRangeWire: 700, meleeRangeWire: 130);

            world.Tick(0.0, nowMs: 0); // Ziel erfassen
            Assert.Equal(player.Id, monster.TargetEntityId);

            (_, _, _, _, List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> attacks, _) = world.Tick(0.0, nowMs: 40);

            Assert.Empty(attacks); // 300 > MeleeRangeWire (130) - kein Angriff, sondern Bewegung
            Assert.True(monster.IsMoving);
            Assert.Equal(player.IntX, monster.TargetX);
            Assert.Equal(player.IntY, monster.TargetY);
        }

        [Fact]
        public void MonsterFarFromAnchor_GivesUpTargetAndMovesBackToAnchor()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, aggressive: false, aggroRangeWire: 0, leashRangeWire: 700);
            monster.Teleport(50000 + 800, 50000); // 800 > LeashRangeWire (700) vom Anker (50000,50000) entfernt
            monster.TargetEntityId = player.Id; // Ziel direkt gesetzt - isoliert den Leine-Check

            world.Tick(0.0, nowMs: 0);

            Assert.Null(monster.TargetEntityId);
            Assert.True(monster.IsMoving);
            Assert.Equal(50000, monster.TargetX);
            Assert.Equal(50000, monster.TargetY);
        }

        [Fact]
        public void MonsterTarget_PlayerDies_DropsTargetAndFallsBackToWander()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, aggressive: false, aggroRangeWire: 0, leashRangeWire: 700);
            monster.TargetEntityId = player.Id;
            world.ApplyDamage(player, damage: 9999, nowMs: 0); // Spieler stirbt, bleibt aber in World (M4a-Respawn)

            world.Tick(0.0, nowMs: 0);

            Assert.Null(monster.TargetEntityId);
        }

        [Fact]
        public void MonsterTarget_PlayerDisconnects_DropsTargetAndFallsBackToWander()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, aggressive: false, aggroRangeWire: 0, leashRangeWire: 700);
            monster.TargetEntityId = player.Id;
            world.Remove(player.Id); // simulierter Disconnect

            world.Tick(0.0, nowMs: 0);

            Assert.Null(monster.TargetEntityId);
        }

        [Fact]
        public void PlayerDiesFromMonsterAttack_ExistingRespawnMechanismApplies()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, x: 50000, y: 50000, maxHp: 5);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, aggressive: true, aggroRangeWire: 400, leashRangeWire: 700, attack: 999);

            world.Tick(0.0, nowMs: 0); // Ziel erfassen
            (_, _, _, _, List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> attacks, _) = world.Tick(0.0, nowMs: 40);

            Assert.Single(attacks);
            Assert.True(attacks[0].Result.TargetDied);
            Assert.Equal(0, player.CurrentHp);
            Assert.Equal(40 + 3000, player.RespawnAtMs); // bestehender M4a-Mechanismus (World.PlayerRespawnDelayMs)
            Assert.True(world.TryGet(player.Id, out _)); // kein Despawn, wie bei M4a
        }

        // ---- M5b: NPCs sind fuer die Monster-KI unsichtbar (siehe docs/tasks/M5b-shop.md Abschnitt 0a/0e) ----

        [Fact]
        public void AggressiveMonster_OnlyNpcNearby_IgnoresNpcAndStaysWithoutTarget()
        {
            var world = new World(maxPlayers: 10);
            world.SpawnNpc(npcId: 1, "Haendler", x: 50000, y: 50000, appearance: default);
            Entity monster = SpawnMonster(world, x: 50000, y: 50000, aggressive: true, aggroRangeWire: 400, leashRangeWire: 700);

            world.Tick(0.0, nowMs: 0);

            Assert.Null(monster.TargetEntityId);
        }

        [Fact]
        public void Npc_NeverWanders_PositionStaysFixedAcrossTicks()
        {
            var world = new World(maxPlayers: 10);
            Entity npc = world.SpawnNpc(npcId: 1, "Haendler", x: 50000, y: 50000, appearance: default);

            world.Tick(0.0, nowMs: 0);
            world.Tick(0.0, nowMs: 1_000_000); // beliebig viel Zeit - RunMonsterAi/RunWanderAi laufen nur fuer Kind=Monster

            Assert.Equal(50000, npc.IntX);
            Assert.Equal(50000, npc.IntY);
            Assert.False(npc.IsMoving);
        }
    }
}
