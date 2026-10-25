using Lumiis.Server.Game;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>Spielbarkeit: tote Spieler duerfen bis zum Respawn weder angreifen, Skills nutzen noch laufen;
    /// Trainingspuppen (FightsBack=false) wehren sich nie.</summary>
    public class WorldPlayabilityTests
    {
        private static Entity JoinPlayer(World world)
        {
            world.TryJoin(1, "Tot", 50000, 50000, out Entity? entity, out _,
                100, 15, 5, 150, 800);
            return entity!;
        }

        private static Entity SpawnMonster(World world)
        {
            return world.SpawnMonster(mobId: 1, "Wildhund", 50000, 50000, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0,
                30, 5, 0, 5, 15);
        }

        [Fact]
        public void TryAttack_DeadAttacker_FailsWithAttackerDead()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            Entity monster = SpawnMonster(world);
            player.CurrentHp = 0;

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(AttackFailedReason.AttackerDead, result.Reason);
            Assert.Equal(30, monster.CurrentHp);
        }

        [Fact]
        public void TryUseSkill_DeadUser_FailsWithUserDead()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            player.CurrentHp = 0;

            SkillUseResult result = world.TryUseSkill(player.Id, skillId: 2, targetEntityId: 0, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(UseSkillFailedReason.UserDead, result.Reason);
        }

        [Fact]
        public void SetDestination_DeadPlayer_DoesNotMove()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            player.CurrentHp = 0;

            Assert.False(world.SetDestination(player.Id, 51000, 50000));
            Assert.False(player.IsMoving);
        }
    
        [Fact]
        public void TryAttack_TrainingDummy_NeverAcquiresTarget()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world);
            Entity dummy = SpawnMonster(world);
            dummy.FightsBack = false;

            AttackResult result = world.TryAttack(player.Id, dummy.Id, nowMs: 0);

            Assert.True(result.Success);
            Assert.Null(dummy.TargetEntityId);
        }
    }
}
