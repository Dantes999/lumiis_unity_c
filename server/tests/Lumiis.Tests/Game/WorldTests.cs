using Lumiis.Server.Game;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    public class WorldTests
    {
        [Fact]
        public void TryJoin_SpawnsAtMapCenter()
        {
            var world = new World(maxPlayers: 10);

            bool ok = world.TryJoin("Bot", out Entity? entity, out _);

            Assert.True(ok);
            Assert.NotNull(entity);
            Assert.Equal(GameConstants.MapWidth / 2, entity!.IntX);
            Assert.Equal(GameConstants.MapHeight / 2, entity.IntY);
        }

        [Fact]
        public void TryJoin_InvalidName_IsRejected()
        {
            var world = new World(maxPlayers: 10);

            bool ok = world.TryJoin("ab", out Entity? entity, out JoinRejectReason reason);

            Assert.False(ok);
            Assert.Null(entity);
            Assert.Equal(JoinRejectReason.InvalidName, reason);
        }

        [Fact]
        public void TryJoin_NameTaken_IsCaseInsensitive()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin("Bot", out _, out _);

            bool ok = world.TryJoin("bot", out Entity? entity, out JoinRejectReason reason);

            Assert.False(ok);
            Assert.Null(entity);
            Assert.Equal(JoinRejectReason.NameTaken, reason);
        }

        [Fact]
        public void TryJoin_ServerFull_IsRejected()
        {
            var world = new World(maxPlayers: 1);
            world.TryJoin("First", out _, out _);

            bool ok = world.TryJoin("Second", out Entity? entity, out JoinRejectReason reason);

            Assert.False(ok);
            Assert.Null(entity);
            Assert.Equal(JoinRejectReason.ServerFull, reason);
        }

        [Fact]
        public void Remove_FreesNameForReuse()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin("Bot", out Entity? entity, out _);

            bool removed = world.Remove(entity!.Id);
            bool rejoined = world.TryJoin("Bot", out _, out _);

            Assert.True(removed);
            Assert.True(rejoined);
        }

        [Fact]
        public void Remove_UnknownId_ReturnsFalse()
        {
            var world = new World(maxPlayers: 10);
            Assert.False(world.Remove(999));
        }

        [Fact]
        public void SetDestination_ClampsToMapBounds()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin("Bot", out Entity? entity, out _);

            world.SetDestination(entity!.Id, -5000, GameConstants.MapHeight + 5000);

            Assert.Equal(0, entity.TargetX);
            Assert.Equal(GameConstants.MapHeight, entity.TargetY);
        }

        [Fact]
        public void SetDestination_UnknownId_ReturnsFalse()
        {
            var world = new World(maxPlayers: 10);
            Assert.False(world.SetDestination(999, 0, 0));
        }

        [Fact]
        public void Tick_ReturnsArrivedEntities()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin("Bot", out Entity? entity, out _);
            world.SetDestination(entity!.Id, entity.IntX + 100, entity.IntY);

            var arrived = world.Tick(10.0); // genug Zeit, um definitiv anzukommen

            Assert.Contains(entity, arrived);
            Assert.False(entity.IsMoving);
        }
    }
}
