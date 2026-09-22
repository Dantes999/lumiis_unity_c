using Lumiis.Server.Game;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    public class WorldTests
    {
        [Fact]
        public void TryJoin_NullPosition_SpawnsAtMapCenter()
        {
            var world = new World(maxPlayers: 10);

            bool ok = world.TryJoin(characterId: 1, "Bot", x: null, y: null, out Entity? entity, out _);

            Assert.True(ok);
            Assert.NotNull(entity);
            Assert.Equal(GameConstants.MapWidth / 2, entity!.IntX);
            Assert.Equal(GameConstants.MapHeight / 2, entity.IntY);
        }

        [Fact]
        public void TryJoin_GivenPosition_SpawnsThere()
        {
            var world = new World(maxPlayers: 10);

            bool ok = world.TryJoin(characterId: 1, "Bot", x: 12345, y: 6789, out Entity? entity, out _);

            Assert.True(ok);
            Assert.Equal(12345, entity!.IntX);
            Assert.Equal(6789, entity.IntY);
        }

        [Fact]
        public void TryJoin_PositionOutsideMap_IsClamped()
        {
            var world = new World(maxPlayers: 10);

            world.TryJoin(characterId: 1, "Bot", x: -5000, y: GameConstants.MapHeight + 5000, out Entity? entity, out _);

            Assert.Equal(0, entity!.IntX);
            Assert.Equal(GameConstants.MapHeight, entity.IntY);
        }

        [Fact]
        public void TryJoin_SameCharacterIdTwice_IsRejectedAsAlreadyOnline()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "Bot", null, null, out _, out _);

            bool ok = world.TryJoin(characterId: 1, "Bot", null, null, out Entity? entity, out JoinRejectReason reason);

            Assert.False(ok);
            Assert.Null(entity);
            Assert.Equal(JoinRejectReason.CharacterAlreadyOnline, reason);
        }

        [Fact]
        public void TryJoin_DifferentCharacterIdsSameName_BothSucceed()
        {
            // World prüft Namens-Eindeutigkeit nicht mehr selbst (seit M2b0 case-insensitiv global per
            // DB-Unique-Index sichergestellt, siehe LumiisDbContext) - zwei verschiedene CharacterIds mit
            // demselben Namen könnten in der Praxis nicht existieren, World selbst hindert es aber nicht.
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "Bot", null, null, out _, out _);

            bool ok = world.TryJoin(characterId: 2, "Bot", null, null, out Entity? entity, out _);

            Assert.True(ok);
            Assert.NotNull(entity);
        }

        [Fact]
        public void TryJoin_ServerFull_IsRejected()
        {
            var world = new World(maxPlayers: 1);
            world.TryJoin(characterId: 1, "First", null, null, out _, out _);

            bool ok = world.TryJoin(characterId: 2, "Second", null, null, out Entity? entity, out JoinRejectReason reason);

            Assert.False(ok);
            Assert.Null(entity);
            Assert.Equal(JoinRejectReason.ServerFull, reason);
        }

        [Fact]
        public void Remove_FreesCharacterIdForRejoin()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(characterId: 1, "Bot", null, null, out Entity? entity, out _);

            bool removed = world.Remove(entity!.Id);
            bool rejoined = world.TryJoin(characterId: 1, "Bot", null, null, out _, out _);

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
            world.TryJoin(characterId: 1, "Bot", null, null, out Entity? entity, out _);

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
            world.TryJoin(characterId: 1, "Bot", null, null, out Entity? entity, out _);
            world.SetDestination(entity!.Id, entity.IntX + 100, entity.IntY);

            var arrived = world.Tick(10.0); // genug Zeit, um definitiv anzukommen

            Assert.Contains(entity, arrived);
            Assert.False(entity.IsMoving);
        }
    }
}
