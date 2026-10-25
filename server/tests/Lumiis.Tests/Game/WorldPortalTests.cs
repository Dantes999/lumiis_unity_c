using System;
using System.Collections.Generic;
using System.Text;
using Lumiis.Data.Maps;
using Lumiis.Server.Game;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>Multi-Map-Spiellogik ohne Netzwerk: Portalerkennung/Cooldown, Karten-Start als Respawn-Ziel
    /// und die Trennung der Entities pro World.</summary>
    public class WorldPortalTests
    {
        private const int Size = 20;

        private static CollisionMap BuildMap()
        {
            int cellCount = Size * Size;
            byte[] packed = new byte[(cellCount + 7) / 8];
            byte[] header = new byte[12];
            Encoding.ASCII.GetBytes("LCM1").CopyTo(header, 0);
            BitConverter.GetBytes(Size).CopyTo(header, 4);
            BitConverter.GetBytes(Size).CopyTo(header, 8);
            byte[] bytes = new byte[header.Length + packed.Length];
            header.CopyTo(bytes, 0);
            packed.CopyTo(bytes, header.Length);
            return CollisionMap.LoadFromBytes(bytes, Size, Size);
        }

        private static MapDefinition VillageWithPortal(string targetMapId = "greenfield")
        {
            var portal = new PortalDefinition(1, 400, 400, 600, 600, targetMapId, 1500, 1500);
            return new MapDefinition("village", "Dorf", BuildMap(), 500, 500, new[] { portal });
        }

        private static World CreateWorld(MapDefinition? definition = null)
        {
            return new World(10, mapDefinition: definition ?? VillageWithPortal());
        }

        [Fact]
        public void CheckPortal_PlayerInsideRectangle_ReturnsPortal()
        {
            World world = CreateWorld();
            Assert.True(world.TryJoin(1, "Alice", 500, 500, out Entity? entity, out _));

            PortalDefinition? portal = world.CheckPortal(entity!, nowMs: 1000);

            Assert.NotNull(portal);
            Assert.Equal("greenfield", portal!.TargetMapId);
        }

        [Fact]
        public void CheckPortal_OutsideRectangle_ReturnsNull()
        {
            World world = CreateWorld();
            Assert.True(world.TryJoin(1, "Alice", 100, 100, out Entity? entity, out _));

            Assert.Null(world.CheckPortal(entity!, nowMs: 1000));
        }

        [Fact]
        public void CheckPortal_Cooldown_PreventsImmediateRetrigger()
        {
            World world = CreateWorld();
            Assert.True(world.TryJoin(1, "Alice", 500, 500, out Entity? entity, out _));

            Assert.NotNull(world.CheckPortal(entity!, nowMs: 1000));
            Assert.Null(world.CheckPortal(entity!, nowMs: 1500));
            Assert.NotNull(world.CheckPortal(entity!, nowMs: 2000));
        }

        [Fact]
        public void CheckPortal_DeadPlayer_DoesNotTrigger()
        {
            World world = CreateWorld();
            Assert.True(world.TryJoin(1, "Alice", 500, 500, out Entity? entity, out _));
            entity!.CurrentHp = 0;

            Assert.Null(world.CheckPortal(entity, nowMs: 1000));
        }

        [Fact]
        public void AddExistingEntity_MovesPlayerBetweenWorlds_WithSameEntityId()
        {
            World village = CreateWorld();
            World greenfield = new World(10, mapDefinition: new MapDefinition("greenfield", "Feld", BuildMap(), 1200, 1200));

            Assert.True(village.TryJoin(1, "Alice", 500, 500, out Entity? entity, out _));
            int entityId = entity!.Id;

            Assert.True(village.Remove(entityId));
            entity.Teleport(1200, 1200);
            greenfield.AddExistingEntity(entity);

            Assert.False(village.TryGet(entityId, out _));
            Assert.True(greenfield.TryGet(entityId, out Entity? moved));
            Assert.Equal(entityId, moved!.Id);
            Assert.Equal("greenfield", moved.MapId);
            Assert.Equal(1200, moved.IntX);
        }

        [Fact]
        public void PlayerRespawn_UsesMapStartInsteadOfMapCenter()
        {
            World world = CreateWorld();
            Assert.True(world.TryJoin(1, "Alice", 100, 100, out Entity? entity, out _));
            entity!.CurrentHp = 0;
            entity.RespawnAtMs = 1;

            List<Entity> respawned = world.Tick(0.04, nowMs: 100).Respawned;

            Assert.Single(respawned);
            Assert.Equal(500, entity.IntX);
            Assert.Equal(500, entity.IntY);
            Assert.Equal(entity.MaxHp, entity.CurrentHp);
        }

        [Fact]
        public void Worlds_HaveSeparateEntitiesAndCollision()
        {
            World village = CreateWorld();
            World greenfield = new World(10, mapDefinition: new MapDefinition("greenfield", "Feld", BuildMap(), 500, 500));
            village.SpawnMonster(1, "Hund", 300, 300, 100, 50, 1000, 2000, 0);

            Assert.Single(village.Entities);
            Assert.Empty(greenfield.Entities);
        }
    }
}
