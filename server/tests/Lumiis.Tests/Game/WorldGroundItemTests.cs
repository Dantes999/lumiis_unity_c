using Lumiis.Server.Game;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>Technischer Boden-Drop: Spawn, Distanz-/Inventarprüfung, Aufheben mit Upgrade-Stufe.</summary>
    public class WorldGroundItemTests
    {
        private static Entity JoinPlayer(World world)
        {
            world.TryJoin(1, "P1", 50000, 50000, out Entity? entity, out _);
            return entity!;
        }

        [Fact]
        public void Pickup_InRange_MovesItemIntoInventory()
        {
            var world = new World(maxPlayers: 10, useGroundDrops: true);
            Entity player = JoinPlayer(world);
            Entity ground = world.SpawnGroundItem(1, 50000, 50000, upgradeLevel: 4);

            (bool success, InventoryActionFailedReason reason) = world.TryPickupItem(player.Id, ground.Id);

            Assert.True(success);
            Assert.Equal(default, reason);
            Assert.Equal(1, player.Inventory[0]);
            Assert.Equal(4, player.InventoryUpgradeLevels[0]);
            Assert.False(world.TryGet(ground.Id, out _));
        }

        [Fact]
        public void Pickup_OutOfRange_IsRejected()
        {
            var world = new World(maxPlayers: 10, useGroundDrops: true);
            Entity player = JoinPlayer(world);
            Entity ground = world.SpawnGroundItem(1, 90000, 90000);

            (bool success, InventoryActionFailedReason reason) = world.TryPickupItem(player.Id, ground.Id);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.PickupOutOfRange, reason);
            Assert.True(world.TryGet(ground.Id, out _));
        }

        [Fact]
        public void Pickup_FullInventory_LeavesGroundItem()
        {
            var world = new World(maxPlayers: 10, useGroundDrops: true);
            Entity player = JoinPlayer(world);
            for (int i = 0; i < player.Inventory.Length; i++) player.Inventory[i] = 1;
            Entity ground = world.SpawnGroundItem(1, 50000, 50000);

            (bool success, InventoryActionFailedReason reason) = world.TryPickupItem(player.Id, ground.Id);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.InventoryFull, reason);
            Assert.True(world.TryGet(ground.Id, out _));
        }
    }
}
