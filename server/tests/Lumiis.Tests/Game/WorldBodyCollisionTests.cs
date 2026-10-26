using Lumiis.Server.Game;
using Lumiis.Shared;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>Körperkollision: Spieler und Monster laufen nicht durcheinander hindurch, Monster auch nicht
    /// durch andere Monster. Spieler untereinander bleiben durchlässig (wie in Metin2, sonst verstopft das Dorf).</summary>
    public class WorldBodyCollisionTests
    {
        private const int X = 50000;
        private const int Y = 50000;
        private const int MinGap = 2 * GameConstants.BodyRadiusWire;

        private static Entity JoinPlayer(World world, int characterId, int x, int y)
        {
            world.TryJoin(characterId, "P" + characterId, x, y, out Entity? entity, out _);
            return entity!;
        }

        private static Entity SpawnMonster(World world, int x, int y, int speedWire = 500) =>
            world.SpawnMonster(mobId: 1, "Wildhund", x, y, speedWire, wanderRadiusWire: 0,
                wanderIntervalMinMs: 1_000_000, wanderIntervalMaxMs: 1_000_000, nowMs: 0, maxHp: 100, attack: 5,
                defense: 0, yangMin: 0, yangMax: 0);

        private static void RunTicks(World world, int ticks)
        {
            for (int i = 0; i < ticks; i++) world.Tick(0.05, nowMs: i * 50L);
        }

        private static long DistanceSq(Entity a, Entity b)
        {
            long dx = a.IntX - b.IntX;
            long dy = a.IntY - b.IntY;
            return dx * dx + dy * dy;
        }

        [Fact]
        public void Player_walking_into_a_monster_stops_at_its_body()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, X, Y);
            Entity monster = SpawnMonster(world, X + 1000, Y);

            world.SetDestination(player.Id, X + 2000, Y);
            RunTicks(world, 60);

            Assert.False(player.IsMoving);
            Assert.True(player.IntX < monster.IntX);
            Assert.True(DistanceSq(player, monster) >= (long)(MinGap - 5) * (MinGap - 5));
        }

        [Fact]
        public void Monster_cannot_walk_through_another_monster()
        {
            var world = new World(maxPlayers: 10);
            Entity walker = SpawnMonster(world, X, Y);
            Entity blocker = SpawnMonster(world, X + 1000, Y);

            world.SetDestination(walker.Id, X + 2000, Y);
            RunTicks(world, 60);

            Assert.True(walker.IntX < blocker.IntX);
            Assert.True(DistanceSq(walker, blocker) >= (long)(MinGap - 5) * (MinGap - 5));
        }

        [Fact]
        public void Members_of_one_monster_group_collide_too()
        {
            var world = new World(maxPlayers: 10);
            Entity walker = SpawnMonster(world, X, Y);
            Entity mate = SpawnMonster(world, X + 1000, Y);
            walker.GroupId = 7;
            mate.GroupId = 7;

            world.SetDestination(walker.Id, X + 2000, Y);
            RunTicks(world, 100);

            Assert.True(walker.IntX < mate.IntX);
            Assert.True(DistanceSq(walker, mate) >= (long)(MinGap - 5) * (MinGap - 5));
        }

        [Fact]
        public void Players_pass_through_each_other()
        {
            var world = new World(maxPlayers: 10);
            Entity walker = JoinPlayer(world, 1, X, Y);
            JoinPlayer(world, 2, X + 1000, Y);

            world.SetDestination(walker.Id, X + 2000, Y);
            RunTicks(world, 100);

            Assert.Equal(X + 2000, walker.IntX);
        }

        [Fact]
        public void Walking_on_against_a_monster_steers_around_it_without_overlapping()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, X, Y);
            Entity monster = SpawnMonster(world, X + MinGap, Y);
            long closestSq = long.MaxValue;

            // Wie gehaltenes WASD: der Client schickt alle 100 ms dasselbe Ziel.
            for (int i = 0; i < 40; i++)
            {
                world.SetDestination(player.Id, X + 2000, Y);
                for (int t = 0; t < 2; t++)
                {
                    world.Tick(0.05, nowMs: (i * 2 + t) * 50L);
                    closestSq = System.Math.Min(closestSq, DistanceSq(player, monster));
                }
            }

            Assert.True(player.IntX > monster.IntX + 100);
            Assert.True(closestSq >= (long)(MinGap - 5) * (MinGap - 5));
        }

        [Fact]
        public void Overlapping_bodies_may_still_move_apart()
        {
            var world = new World(maxPlayers: 10);
            Entity player = JoinPlayer(world, 1, X, Y);
            SpawnMonster(world, X + 10, Y);

            world.SetDestination(player.Id, X - 1000, Y);
            RunTicks(world, 60);

            Assert.Equal(X - 1000, player.IntX);
        }
    }
}
