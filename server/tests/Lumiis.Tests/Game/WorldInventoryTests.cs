using System;
using System.Collections.Generic;
using Lumiis.Data.Items;
using Lumiis.Server.Game;
using Lumiis.Shared.Net;
using Lumiis.Tests;
using Xunit;

namespace Lumiis.Tests.Game
{
    public class WorldInventoryTests
    {
        private const int PlayerMaxHp = 100;
        private const int PlayerAttack = 15;
        private const int PlayerDefense = 5;

        // id1: Waffe (+5 Angriff), id2: Waffe (+8 Angriff), id3: Ruestung (+4 Verteidigung),
        // id4: Heiltrank (+20 Leben).
        private const string ItemsJson = @"{
  ""items"": [
    { ""id"": 1, ""name"": ""Schwert A"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 5, ""defenseBonus"": 0 },
    { ""id"": 2, ""name"": ""Schwert B"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 8, ""defenseBonus"": 0 },
    { ""id"": 3, ""name"": ""Ruestung"", ""itemType"": ""Armor"", ""lookId"": 1, ""attackBonus"": 0, ""defenseBonus"": 4 },
    { ""id"": 4, ""name"": ""Trank"", ""itemType"": ""Consumable"", ""healAmount"": 20 }
  ]
}";

        private static World CreateWorld(Random? random = null)
        {
            ItemCatalog catalog = ItemCatalog.LoadFromJson(ItemsJson, TestLookCatalogs.CreateMinimal());
            return new World(maxPlayers: 10, collisionMap: null, random: random, itemCatalog: catalog);
        }

        private static Entity JoinPlayer(World world, int characterId = 1, int maxHp = PlayerMaxHp, int attack = PlayerAttack, int defense = PlayerDefense)
        {
            world.TryJoin(characterId, "P" + characterId, 50000, 50000, out Entity? entity, out _, maxHp, attack, defense);
            return entity!;
        }

        // ---- TryEquip ----

        [Fact]
        public void TryEquip_EmptyEquipmentSlot_EquipsItemAndClearsInventorySlot()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world);
            player.Inventory[0] = 1; // Schwert A, +5 Angriff

            (bool success, InventoryActionFailedReason reason) = world.TryEquip(player.Id, 0);

            Assert.True(success);
            Assert.Equal(1, player.EquippedWeaponItemId);
            Assert.Equal(0, player.Inventory[0]); // Slot jetzt leer, nichts war vorher ausgeruestet.
            Assert.Equal(PlayerAttack + 5, player.Attack);
        }

        [Fact]
        public void TryEquip_SlotAlreadyEquipped_SwapsPreviousItemBackIntoSameSlot()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world);
            player.Inventory[0] = 1; // Schwert A, +5
            world.TryEquip(player.Id, 0);
            Assert.Equal(PlayerAttack + 5, player.Attack);

            player.Inventory[1] = 2; // Schwert B, +8
            (bool success, _) = world.TryEquip(player.Id, 1);

            Assert.True(success);
            Assert.Equal(2, player.EquippedWeaponItemId);
            Assert.Equal(1, player.Inventory[1]); // Schwert A kommt in denselben Slot zurueck.
            Assert.Equal(PlayerAttack + 8, player.Attack);
        }

        [Fact]
        public void TryEquip_EmptyInventorySlot_ReturnsEmptySlot()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world);

            (bool success, InventoryActionFailedReason reason) = world.TryEquip(player.Id, 0);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.EmptySlot, reason);
        }

        [Fact]
        public void TryEquip_ConsumableItem_ReturnsWrongItemType()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world);
            player.Inventory[0] = 4; // Trank

            (bool success, InventoryActionFailedReason reason) = world.TryEquip(player.Id, 0);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.WrongItemType, reason);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(20)]
        public void TryEquip_InvalidSlotIndex_ReturnsInvalidSlot(int slotIndex)
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world);

            (bool success, InventoryActionFailedReason reason) = world.TryEquip(player.Id, slotIndex);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.InvalidSlot, reason);
        }

        // ---- TryUnequip ----

        [Fact]
        public void TryUnequip_EquippedItem_MovesToFirstFreeSlotAndRecalculatesStats()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world);
            player.Inventory[5] = 3; // Ruestung, +4 Verteidigung
            world.TryEquip(player.Id, 5);
            Assert.Equal(PlayerDefense + 4, player.Defense);

            (bool success, _) = world.TryUnequip(player.Id, EquipmentSlot.Armor);

            Assert.True(success);
            Assert.Equal(0, player.EquippedArmorItemId);
            Assert.Equal(3, player.Inventory[0]); // erste freie Stelle.
            Assert.Equal(PlayerDefense, player.Defense);
        }

        [Fact]
        public void TryUnequip_NothingEquipped_ReturnsEmptySlot()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world);

            (bool success, InventoryActionFailedReason reason) = world.TryUnequip(player.Id, EquipmentSlot.Weapon);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.EmptySlot, reason);
        }

        [Fact]
        public void TryUnequip_NoFreeInventorySlot_ReturnsInventoryFull()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world);
            player.Inventory[0] = 1;
            world.TryEquip(player.Id, 0);
            for (int i = 0; i < player.Inventory.Length; i++)
                player.Inventory[i] = 4; // Inventar komplett fuellen.

            (bool success, InventoryActionFailedReason reason) = world.TryUnequip(player.Id, EquipmentSlot.Weapon);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.InventoryFull, reason);
            Assert.Equal(1, player.EquippedWeaponItemId); // unveraendert, kein Teilerfolg.
        }

        // ---- TryUseConsumable ----

        [Fact]
        public void TryUseConsumable_HealsAndRemovesItem()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, maxHp: 100);
            player.CurrentHp = 50;
            player.Inventory[0] = 4; // Trank, +20

            (bool success, _) = world.TryUseConsumable(player.Id, 0, nowMs: 0);

            Assert.True(success);
            Assert.Equal(70, player.CurrentHp);
            Assert.Equal(0, player.Inventory[0]);
        }

        [Fact]
        public void TryUseConsumable_CapsAtMaxHp()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, maxHp: 100);
            player.CurrentHp = 90;
            player.Inventory[0] = 4; // Trank, +20 -> waere 110, gedeckelt bei 100.

            world.TryUseConsumable(player.Id, 0, nowMs: 0);

            Assert.Equal(100, player.CurrentHp);
        }

        [Fact]
        public void TryUseConsumable_AlreadyAtFullHp_ReturnsTargetAtFullHp()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, maxHp: 100);
            player.Inventory[0] = 4;

            (bool success, InventoryActionFailedReason reason) = world.TryUseConsumable(player.Id, 0, nowMs: 0);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.TargetAtFullHp, reason);
            Assert.Equal(4, player.Inventory[0]); // Item bleibt erhalten.
        }

        [Fact]
        public void TryUseConsumable_EquipmentItem_ReturnsNotConsumable()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, maxHp: 100);
            player.CurrentHp = 50;
            player.Inventory[0] = 1; // Waffe, kein Consumable.

            (bool success, InventoryActionFailedReason reason) = world.TryUseConsumable(player.Id, 0, nowMs: 0);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.NotConsumable, reason);
        }

        // ---- TryAddItemToInventory ----

        [Fact]
        public void TryAddItemToInventory_UsesFirstFreeSlot()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world);
            player.Inventory[0] = 1;

            bool added = world.TryAddItemToInventory(player.Id, 4);

            Assert.True(added);
            Assert.Equal(4, player.Inventory[1]);
        }

        [Fact]
        public void TryAddItemToInventory_InventoryFull_ReturnsFalse()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world);
            for (int i = 0; i < player.Inventory.Length; i++)
                player.Inventory[i] = 1;

            bool added = world.TryAddItemToInventory(player.Id, 4);

            Assert.False(added);
        }

        // ---- Drops bei Monstertod (siehe World.TryAttack) ----

        private static Entity SpawnMonsterWithDrops(World world, IReadOnlyList<(int ItemId, int ChancePercent)> drops, int maxHp = 10)
        {
            return world.SpawnMonster(mobId: 1, "Wildhund", x: 50000, y: 50000, speedWire: 700,
                wanderRadiusWire: 500, wanderIntervalMinMs: 1000, wanderIntervalMaxMs: 1000, nowMs: 0,
                maxHp: maxHp, attack: 5, defense: 0, yangMin: 0, yangMax: 0, drops: drops);
        }

        [Fact]
        public void TryAttack_KillWithGuaranteedDrop_AddsItemToAttackerInventory()
        {
            // chancePercent=100: _random.Next(100) liegt immer in [0,99[, die Bedingung ">= 100" ist nie
            // wahr - deterministisch unabhaengig vom Seed, kein Random-Mock noetig.
            World world = CreateWorld();
            Entity player = JoinPlayer(world, attack: 999);
            Entity monster = SpawnMonsterWithDrops(world, new List<(int, int)> { (4, 100) });

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.True(result.TargetDied);
            Assert.Equal(new[] { 4 }, result.DroppedItemIds);
            Assert.False(result.ItemLostToFullInventory);
            Assert.Equal(4, player.Inventory[0]);
        }

        [Fact]
        public void TryAttack_KillWithImpossibleDrop_NoItemAdded()
        {
            // chancePercent=0: die Bedingung ">= 0" ist immer wahr - deterministisch kein Treffer.
            World world = CreateWorld();
            Entity player = JoinPlayer(world, attack: 999);
            Entity monster = SpawnMonsterWithDrops(world, new List<(int, int)> { (4, 0) });

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.True(result.TargetDied);
            Assert.Empty(result.DroppedItemIds);
            Assert.False(result.ItemLostToFullInventory);
            Assert.All(player.Inventory, itemId => Assert.Equal(0, itemId));
        }

        [Fact]
        public void TryAttack_KillWithDropButFullInventory_ReportsItemLostAndKillStaysValid()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, attack: 999);
            for (int i = 0; i < player.Inventory.Length; i++)
                player.Inventory[i] = 1; // Inventar komplett fuellen.
            Entity monster = SpawnMonsterWithDrops(world, new List<(int, int)> { (4, 100) });

            AttackResult result = world.TryAttack(player.Id, monster.Id, nowMs: 0);

            Assert.True(result.TargetDied); // Kill bleibt gueltig trotz verlorenem Drop.
            Assert.Empty(result.DroppedItemIds);
            Assert.True(result.ItemLostToFullInventory);
        }
    }
}
