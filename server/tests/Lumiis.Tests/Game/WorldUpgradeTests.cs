using System;
using Lumiis.Data.Items;
using Lumiis.Data.Upgrade;
using Lumiis.Server.Game;
using Lumiis.Shared.Net;
using Lumiis.Tests;
using Xunit;

namespace Lumiis.Tests.Game
{
    public class WorldUpgradeTests
    {
        private const int PlayerAttack = 15;
        private const int PlayerDefense = 5;

        // id1: Waffe (+5 Angriff, +2 je Stufe), id2: Ruestung (+4 Verteidigung, +1 je Stufe),
        // id9: Schleifstein, id10: Bewahrungsrolle.
        private const string ItemsJson = @"{
  ""items"": [
    { ""id"": 1, ""name"": ""Schwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 5, ""defenseBonus"": 0, ""upgradeBonusPerLevel"": 2 },
    { ""id"": 2, ""name"": ""Ruestung"", ""itemType"": ""Armor"", ""lookId"": 1, ""attackBonus"": 0, ""defenseBonus"": 4, ""upgradeBonusPerLevel"": 1 },
    { ""id"": 9, ""name"": ""Schleifstein"", ""itemType"": ""UpgradeMaterial"" },
    { ""id"": 10, ""name"": ""Bewahrungsrolle"", ""itemType"": ""ProtectionScroll"" }
  ]
}";

        private const string UpgradeJson = @"{
  ""maxUpgradeLevel"": 9,
  ""successChancePercent"": [ 90, 80, 70, 60, 50, 40, 30, 20, 10 ],
  ""whetstoneItemId"": 9,
  ""protectionScrollItemId"": 10
}";

        /// <summary>Deterministische Zufallsquelle: Next(...) liefert immer denselben Wert. World.TryUpgrade
        /// ruft genau einmal _random.Next(100) auf - 0 = Erfolg (jede Chance &gt; 0), 99 = Fehlschlag (keine
        /// Chance ist 100).</summary>
        private sealed class FixedRandom : Random
        {
            private readonly int _value;

            public FixedRandom(int value)
            {
                _value = value;
            }

            public override int Next(int maxValue)
            {
                return Math.Min(_value, Math.Max(0, maxValue - 1));
            }
        }

        private static World CreateWorld(int roll)
        {
            ItemCatalog items = ItemCatalog.LoadFromJson(ItemsJson, TestLookCatalogs.CreateMinimal());
            UpgradeCatalog upgrades = UpgradeCatalog.LoadFromJson(UpgradeJson, items);
            return new World(maxPlayers: 10, collisionMap: null, random: new FixedRandom(roll),
                itemCatalog: items, upgradeCatalog: upgrades);
        }

        private static Entity JoinPlayer(World world)
        {
            world.TryJoin(1, "P1", 50000, 50000, out Entity? entity, out _, 100, PlayerAttack, PlayerDefense);
            return entity!;
        }

        private static void EquipWeapon(World world, Entity player, int upgradeLevel)
        {
            player.SetEquipped(EquipmentSlot.Weapon, 1, upgradeLevel);
            world.RecomputeDerivedStats(player.Id);
        }

        [Fact]
        public void TryUpgrade_Success_IncreasesLevelAndAttackByBonusPerLevel_AndConsumesWhetstone()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            EquipWeapon(world, player, upgradeLevel: 2);
            player.Inventory[0] = 9; // Schleifstein

            UpgradeAttemptResult result = world.TryUpgrade(player.Id, EquipmentSlot.Weapon, useProtectionScroll: false, nowMs: 0);

            Assert.False(result.Rejected);
            Assert.True(result.Success);
            Assert.False(result.Destroyed);
            Assert.Equal(3, result.NewUpgradeLevel);
            Assert.Equal(3, player.EquippedWeaponUpgradeLevel);
            Assert.Equal(0, player.Inventory[0]); // Schleifstein verbraucht.
            Assert.Equal(PlayerAttack + 5 + 3 * 2, player.Attack); // +5 Basis, +2 je Stufe (jetzt Stufe 3).
        }

        [Fact]
        public void TryUpgrade_Success_OnArmor_IncreasesDefense()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            player.SetEquipped(EquipmentSlot.Armor, 2, 0);
            player.Inventory[0] = 9;

            UpgradeAttemptResult result = world.TryUpgrade(player.Id, EquipmentSlot.Armor, useProtectionScroll: false, nowMs: 0);

            Assert.True(result.Success);
            Assert.Equal(1, player.EquippedArmorUpgradeLevel);
            Assert.Equal(PlayerDefense + 4 + 1, player.Defense);
        }

        [Fact]
        public void TryUpgrade_Success_FromLevel0_AttackUsesNewLevel()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            EquipWeapon(world, player, upgradeLevel: 0);
            player.Inventory[0] = 9;

            world.TryUpgrade(player.Id, EquipmentSlot.Weapon, useProtectionScroll: false, nowMs: 0);

            Assert.Equal(1, player.EquippedWeaponUpgradeLevel);
            Assert.Equal(PlayerAttack + 5 + 2, player.Attack);
        }

        [Fact]
        public void TryUpgrade_EmptySlot_RejectedWithoutConsumingMaterial()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            player.Inventory[0] = 9;

            UpgradeAttemptResult result = world.TryUpgrade(player.Id, EquipmentSlot.Weapon, useProtectionScroll: false, nowMs: 0);

            Assert.True(result.Rejected);
            Assert.Equal(UpgradeActionFailedReason.SlotEmpty, result.FailureReason);
            Assert.Equal(9, player.Inventory[0]);
        }

        [Fact]
        public void TryUpgrade_MaxLevel_RejectedWithoutConsumingMaterial()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            EquipWeapon(world, player, upgradeLevel: 9);
            player.Inventory[0] = 9;
            player.Inventory[1] = 10;

            UpgradeAttemptResult result = world.TryUpgrade(player.Id, EquipmentSlot.Weapon, useProtectionScroll: true, nowMs: 0);

            Assert.True(result.Rejected);
            Assert.Equal(UpgradeActionFailedReason.AlreadyMaxLevel, result.FailureReason);
            Assert.Equal(9, player.Inventory[0]);
            Assert.Equal(10, player.Inventory[1]);
            Assert.Equal(9, player.EquippedWeaponUpgradeLevel);
        }

        [Fact]
        public void TryUpgrade_NoWhetstone_RejectedWithoutConsumingProtectionScroll()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            EquipWeapon(world, player, upgradeLevel: 0);
            player.Inventory[0] = 10; // nur Rolle, kein Schleifstein

            UpgradeAttemptResult result = world.TryUpgrade(player.Id, EquipmentSlot.Weapon, useProtectionScroll: true, nowMs: 0);

            Assert.True(result.Rejected);
            Assert.Equal(UpgradeActionFailedReason.NoMaterial, result.FailureReason);
            Assert.Equal(10, player.Inventory[0]); // Rolle bleibt erhalten.
            Assert.Equal(0, player.EquippedWeaponUpgradeLevel);
        }

        [Fact]
        public void TryUpgrade_ProtectionRequestedButMissing_RejectedAndNothingConsumed()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            EquipWeapon(world, player, upgradeLevel: 4);
            player.Inventory[0] = 9; // Schleifstein vorhanden, Rolle NICHT

            UpgradeAttemptResult result = world.TryUpgrade(player.Id, EquipmentSlot.Weapon, useProtectionScroll: true, nowMs: 0);

            Assert.True(result.Rejected);
            Assert.Equal(UpgradeActionFailedReason.NoProtectionScroll, result.FailureReason);
            Assert.Equal(9, player.Inventory[0]); // explizit: Schleifstein NICHT verbraucht.
            Assert.Equal(4, player.EquippedWeaponUpgradeLevel);
            Assert.Equal(1, player.EquippedWeaponItemId);
        }

        [Fact]
        public void TryUpgrade_ForcedFailureWithoutProtection_DestroysItemAndDropsAttack()
        {
            World world = CreateWorld(roll: 99);
            Entity player = JoinPlayer(world);
            EquipWeapon(world, player, upgradeLevel: 3);
            Assert.Equal(PlayerAttack + 5 + 3 * 2, player.Attack);
            player.Inventory[0] = 9;

            UpgradeAttemptResult result = world.TryUpgrade(player.Id, EquipmentSlot.Weapon, useProtectionScroll: false, nowMs: 0);

            Assert.False(result.Rejected);
            Assert.False(result.Success);
            Assert.True(result.Destroyed);
            Assert.Equal(0, player.EquippedWeaponItemId);
            Assert.Equal(0, player.EquippedWeaponUpgradeLevel);
            Assert.Equal(PlayerAttack, player.Attack);
            Assert.Equal(0, player.Inventory[0]); // Schleifstein trotzdem verbraucht.
        }

        [Fact]
        public void TryUpgrade_ForcedFailureWithProtection_KeepsItemButConsumesWhetstoneAndScroll()
        {
            World world = CreateWorld(roll: 99);
            Entity player = JoinPlayer(world);
            EquipWeapon(world, player, upgradeLevel: 3);
            player.Inventory[0] = 9;
            player.Inventory[1] = 10;

            UpgradeAttemptResult result = world.TryUpgrade(player.Id, EquipmentSlot.Weapon, useProtectionScroll: true, nowMs: 0);

            Assert.False(result.Rejected);
            Assert.False(result.Success);
            Assert.False(result.Destroyed);
            Assert.Equal(3, result.NewUpgradeLevel);
            Assert.Equal(1, player.EquippedWeaponItemId);
            Assert.Equal(3, player.EquippedWeaponUpgradeLevel);
            Assert.Equal(PlayerAttack + 5 + 3 * 2, player.Attack);
            Assert.Equal(0, player.Inventory[0]); // Schleifstein verbraucht.
            Assert.Equal(0, player.Inventory[1]); // Bewahrungsrolle ebenfalls verbraucht.
        }

        [Fact]
        public void EquipThenUnequip_KeepsUpgradeLevelWithTheItem()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            player.Inventory[0] = 1;
            player.InventoryUpgradeLevels[0] = 5;

            (bool equipped, _) = world.TryEquip(player.Id, 0);
            Assert.True(equipped);
            Assert.Equal(5, player.EquippedWeaponUpgradeLevel);
            Assert.Equal(PlayerAttack + 5 + 5 * 2, player.Attack);

            (bool unequipped, _) = world.TryUnequip(player.Id, EquipmentSlot.Weapon);
            Assert.True(unequipped);
            Assert.Equal(0, player.EquippedWeaponItemId);
            Assert.Equal(0, player.EquippedWeaponUpgradeLevel);
            Assert.Equal(1, player.Inventory[0]);
            Assert.Equal(5, player.InventoryUpgradeLevels[0]);
        }

        [Fact]
        public void Equip_SwappingWeapons_SwapsUpgradeLevels()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            player.Inventory[0] = 1; // Schwert mit +4
            player.InventoryUpgradeLevels[0] = 4;
            world.TryEquip(player.Id, 0);

            player.Inventory[1] = 1; // zweites Schwert mit +7
            player.InventoryUpgradeLevels[1] = 7;
            world.TryEquip(player.Id, 1);

            Assert.Equal(7, player.EquippedWeaponUpgradeLevel);
            Assert.Equal(4, player.InventoryUpgradeLevels[1]);
        }

        [Fact]
        public void TryEquip_UpgradeMaterial_ReturnsWrongItemType()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            player.Inventory[0] = 9;

            (bool success, InventoryActionFailedReason reason) = world.TryEquip(player.Id, 0);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.WrongItemType, reason);
        }

        [Fact]
        public void TryUseConsumable_UpgradeMaterial_ReturnsNotConsumable()
        {
            World world = CreateWorld(roll: 0);
            Entity player = JoinPlayer(world);
            player.CurrentHp = 50;
            player.Inventory[0] = 9;

            (bool success, InventoryActionFailedReason reason) = world.TryUseConsumable(player.Id, 0, nowMs: 0);

            Assert.False(success);
            Assert.Equal(InventoryActionFailedReason.NotConsumable, reason);
        }
    }
}
