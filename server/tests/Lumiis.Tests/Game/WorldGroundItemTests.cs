using System.Collections.Generic;
using System.Linq;
using Lumiis.Server.Game;
using Lumiis.Shared;
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
    
        private static Entity JoinFighter(World world, int characterId, int x = 50000, int y = 50000)
        {
            world.TryJoin(characterId, "P" + characterId, x, y, out Entity? entity, out _, 100, 999, 0);
            return entity!;
        }

        private static Entity SpawnDropMonster(World world, params int[] guaranteedItemIds)
        {
            return world.SpawnMonster(mobId: 1, "Wildhund", x: 50000, y: 50000, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0,
                maxHp: 10, attack: 5, defense: 0, drops: guaranteedItemIds.Select(id => (id, 100)).ToList());
        }

        [Fact]
        public void Attack_OnGroundItem_IsRejectedAsNotAttackable()
        {
            var world = new World(maxPlayers: 10, useGroundDrops: true);
            Entity player = JoinFighter(world, 1);
            Entity ground = world.SpawnGroundItem(1, 50000, 50000);

            AttackResult result = world.TryAttack(player.Id, ground.Id, nowMs: 0);

            Assert.False(result.Success);
            Assert.Equal(AttackFailedReason.TargetNotAttackable, result.Reason);
            Assert.True(world.TryGet(ground.Id, out _));
        }

        [Fact]
        public void Kill_WithGroundDrops_SpawnsOwnedItemsSpreadAroundCorpse()
        {
            var world = new World(maxPlayers: 10, useGroundDrops: true);
            Entity player = JoinFighter(world, 1);
            Entity monster = SpawnDropMonster(world, 4, 5, 6);

            world.TryAttack(player.Id, monster.Id, nowMs: 1000);

            List<Entity> drops = world.Entities.Where(e => e.Kind == EntityKind.GroundItem).ToList();
            Assert.Equal(3, drops.Count);
            Assert.Equal(3, drops.Select(d => (d.IntX, d.IntY)).Distinct().Count());
            Assert.All(drops, d =>
            {
                Assert.Equal(player.Id, d.LootOwnerEntityId);
                Assert.Equal(1000 + GameConstants.GroundItemOwnershipMs, d.LootOwnershipEndsAtMs);
                Assert.Equal(1000 + GameConstants.GroundItemLifetimeMs, d.DespawnAtMs);
            });
            Assert.All(player.Inventory, itemId => Assert.Equal(0, itemId));
        }

        [Fact]
        public void Pickup_ByStranger_RejectedDuringOwnership_AllowedAfterwards()
        {
            var world = new World(maxPlayers: 10, useGroundDrops: true);
            Entity owner = JoinFighter(world, 1);
            Entity stranger = JoinFighter(world, 2);
            Entity ground = world.SpawnGroundItem(1, 50000, 50000, ownerEntityId: owner.Id, nowMs: 0);

            Assert.Equal(InventoryActionFailedReason.PickupNotOwner,
                world.TryPickupItem(stranger.Id, ground.Id, nowMs: GameConstants.GroundItemOwnershipMs - 1).Reason);

            Assert.True(world.TryPickupItem(stranger.Id, ground.Id, nowMs: GameConstants.GroundItemOwnershipMs).Success);
            Assert.Equal(1, stranger.Inventory[0]);
        }

        [Fact]
        public void Pickup_ByOwnerOrPartyMember_AllowedDuringOwnership()
        {
            var world = new World(maxPlayers: 10, useGroundDrops: true);
            Entity owner = JoinFighter(world, 1);
            Entity partyMember = JoinFighter(world, 2);
            Entity first = world.SpawnGroundItem(1, 50000, 50000, ownerEntityId: owner.Id, nowMs: 0);
            Entity second = world.SpawnGroundItem(1, 50000, 50000, ownerEntityId: owner.Id, nowMs: 0);

            Assert.True(world.TryPickupItem(owner.Id, first.Id, nowMs: 1).Success);
            Assert.True(world.TryPickupItem(partyMember.Id, second.Id, nowMs: 1, sharesLootWith: id => id == owner.Id).Success);
        }

        [Fact]
        public void RemoveExpiredGroundItems_RemovesOnlyExpiredDrops()
        {
            var world = new World(maxPlayers: 10, useGroundDrops: true);
            Entity old = world.SpawnGroundItem(1, 50000, 50000, nowMs: 0);
            Entity fresh = world.SpawnGroundItem(1, 50000, 50000, nowMs: 1000);
            Entity permanent = world.SpawnGroundItem(1, 50000, 50000);

            List<int> expired = world.RemoveExpiredGroundItems(GameConstants.GroundItemLifetimeMs);

            Assert.Equal(new[] { old.Id }, expired);
            Assert.False(world.TryGet(old.Id, out _));
            Assert.True(world.TryGet(fresh.Id, out _));
            Assert.True(world.TryGet(permanent.Id, out _));
        }
    }
}
