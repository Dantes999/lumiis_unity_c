using System.Linq;
using Lumiis.Server.Game;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>Flächenschlag (Protocol 23): ein normaler Spieler-Hieb trifft alle Gegner im Schlagbogen.</summary>
    public class WorldSwingTests
    {
        private const int X = 50000;
        private const int Y = 50000;

        private static Entity JoinPlayer(World world, int characterId = 1, int x = X, int y = Y, int attack = 15)
        {
            world.TryJoin(characterId, "P" + characterId, x, y, out Entity? entity, out _,
                maxHp: 100, attack: attack, defense: 5, meleeRangeWire: 150, attackCooldownMs: 800);
            return entity!;
        }

        private static Entity SpawnMonster(World world, int x, int y, int maxHp = 100) =>
            world.SpawnMonster(mobId: 1, "Wildhund", x, y, speedWire: 700, wanderRadiusWire: 500,
                wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0, maxHp, attack: 5, defense: 0,
                yangMin: 0, yangMax: 0);

        [Fact]
        public void Swing_HitsEveryMonsterInFrontArc_WithFullDamage()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            Entity right = SpawnMonster(world, X + 120, Y);
            Entity upRight = SpawnMonster(world, X + 90, Y + 90);
            Entity downRight = SpawnMonster(world, X + 80, Y - 100);

            SwingResult swing = world.TryPlayerSwing(player.Id, right.Id, aimX: 1000, aimY: 0, nowMs: 0);

            Assert.True(swing.Success);
            Assert.Equal(new[] { right.Id, upRight.Id, downRight.Id }.OrderBy(i => i),
                swing.Hits.Select(h => h.TargetEntityId).OrderBy(i => i));
            Assert.All(swing.Hits, h => Assert.Equal(15, h.Result.Damage));
            Assert.All(new[] { right, upRight, downRight }, m => Assert.Equal(85, m.CurrentHp));
            // Jeder Getroffene nimmt den Spieler ins Visier (Splasher: Hieb zieht alle an).
            Assert.All(new[] { right, upRight, downRight }, m => Assert.Equal(player.Id, m.TargetEntityId));
        }

        [Fact]
        public void Swing_IgnoresMonstersBehindAndOutOfRange()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            Entity front = SpawnMonster(world, X + 120, Y);
            Entity behind = SpawnMonster(world, X - 120, Y);
            Entity far = SpawnMonster(world, X + 400, Y);

            SwingResult swing = world.TryPlayerSwing(player.Id, preferredTargetId: 0, aimX: 1, aimY: 0, nowMs: 0);

            Assert.True(swing.Success);
            Assert.Equal(new[] { front.Id }, swing.Hits.Select(h => h.TargetEntityId));
            Assert.Equal(100, behind.CurrentHp);
            Assert.Equal(100, far.CurrentHp);
        }

        [Fact]
        public void Swing_WithoutAim_UsesDirectionToPreferredTarget()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            Entity target = SpawnMonster(world, X, Y + 120);
            Entity behind = SpawnMonster(world, X, Y - 120);

            SwingResult swing = world.TryPlayerSwing(player.Id, target.Id, 0, 0, nowMs: 0);

            Assert.Equal(new[] { target.Id }, swing.Hits.Select(h => h.TargetEntityId));
            Assert.Equal(100, behind.CurrentHp);
        }

        [Fact]
        public void Swing_HitsMonsterStandingInsideThePlayer_RegardlessOfAngle()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            Entity onTop = SpawnMonster(world, X - 30, Y);

            SwingResult swing = world.TryPlayerSwing(player.Id, 0, aimX: 1, aimY: 0, nowMs: 0);

            Assert.True(swing.Success);
            Assert.Equal(new[] { onTop.Id }, swing.Hits.Select(h => h.TargetEntityId));
        }

        [Fact]
        public void Swing_NothingInArc_FailsWithoutConsumingCooldown()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            Entity behind = SpawnMonster(world, X - 120, Y);

            SwingResult miss = world.TryPlayerSwing(player.Id, 0, aimX: 1, aimY: 0, nowMs: 0);
            Assert.False(miss.Success);
            Assert.Equal(AttackFailedReason.OutOfRange, miss.Reason);

            SwingResult hit = world.TryPlayerSwing(player.Id, 0, aimX: -1, aimY: 0, nowMs: 1);
            Assert.True(hit.Success);
            Assert.Equal(behind.Id, hit.Hits.Single().TargetEntityId);
        }

        [Fact]
        public void Swing_RespectsCooldown_ForTheWholeArc()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            SpawnMonster(world, X + 120, Y);
            SpawnMonster(world, X + 100, Y + 50);

            Assert.True(world.TryPlayerSwing(player.Id, 0, 1, 0, nowMs: 0).Success);
            SwingResult again = world.TryPlayerSwing(player.Id, 0, 1, 0, nowMs: 400);

            Assert.False(again.Success);
            Assert.Equal(AttackFailedReason.OnCooldown, again.Reason);
            Assert.True(world.TryPlayerSwing(player.Id, 0, 1, 0, nowMs: 800).Success);
        }

        [Fact]
        public void Swing_KillsSeveralMonstersAtOnce()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, attack: 50);
            Entity a = SpawnMonster(world, X + 100, Y, maxHp: 40);
            Entity b = SpawnMonster(world, X + 100, Y + 60, maxHp: 40);

            SwingResult swing = world.TryPlayerSwing(player.Id, 0, 1, 0, nowMs: 0);

            Assert.All(swing.Hits, h => Assert.True(h.Result.TargetDied));
            Assert.DoesNotContain(world.Entities, e => e.Id == a.Id);
            Assert.DoesNotContain(world.Entities, e => e.Id == b.Id);
        }

        [Fact]
        public void Swing_PreferredTargetReasons_MatchTryAttack()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, characterId: 1);
            Entity other = JoinPlayer(world, characterId: 2, x: X + 100);

            Assert.Equal(AttackFailedReason.TargetIsPlayer, world.TryPlayerSwing(player.Id, other.Id, 0, 0, 0).Reason);
            Assert.Equal(AttackFailedReason.TargetNotFound, world.TryPlayerSwing(player.Id, 999, 0, 0, 0).Reason);
        }

        [Fact]
        public void Swing_CapsNumberOfTargets()
        {
            var world = new World(maxPlayers: 30);
            Entity player = JoinPlayer(world);
            for (int i = 0; i < 20; i++)
                SpawnMonster(world, X + 100, Y + i - 10);

            SwingResult swing = world.TryPlayerSwing(player.Id, 0, 1, 0, nowMs: 0);

            Assert.Equal(Lumiis.Shared.GameConstants.PlayerSwingMaxTargets, swing.Hits.Count);
        }
    }
}
