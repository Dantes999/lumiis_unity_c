using System.Collections.Generic;
using Lumiis.Data.Items;
using Lumiis.Server.Game;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Lumiis.Tests;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>TryOpenShop/TryBuy/TrySell (M5b, kein Netzwerkbezug, siehe docs/tasks/M5b-shop.md Abschnitt 4) -
    /// analog zu WorldInventoryTests.</summary>
    public class WorldShopTests
    {
        private const int PlayerMaxHp = 100;

        // id1: Waffe, kaufbar (200/80). id2: Trank, kaufbar, nicht verkaeuflich (20/0). id3: Waffe, existiert
        // im Katalog, aber NICHT im Sortiment des Test-NPCs (fuer ItemNotInStock).
        private const string ItemsJson = @"{
  ""items"": [
    { ""id"": 1, ""name"": ""Schwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 5, ""defenseBonus"": 0, ""buyPrice"": 200, ""sellPrice"": 80 },
    { ""id"": 2, ""name"": ""Trank"", ""itemType"": ""Consumable"", ""healAmount"": 10, ""buyPrice"": 20, ""sellPrice"": 0 },
    { ""id"": 3, ""name"": ""Ausserhalb"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 1, ""defenseBonus"": 0, ""buyPrice"": 50, ""sellPrice"": 20 }
  ]
}";

        private static World CreateWorld()
        {
            ItemCatalog catalog = ItemCatalog.LoadFromJson(ItemsJson, TestLookCatalogs.CreateMinimal());
            return new World(maxPlayers: 10, collisionMap: null, itemCatalog: catalog);
        }

        private static Entity JoinPlayer(World world, int x, int y, long yang = 0, int characterId = 1)
        {
            world.TryJoin(characterId, "P" + characterId, x, y, out Entity? entity, out _, PlayerMaxHp);
            entity!.Yang = yang;
            return entity;
        }

        private static Entity SpawnNpc(World world, int x, int y, IReadOnlyList<int>? shopItemIds = null) =>
            world.SpawnNpc(npcId: 1, "Haendler", x, y, appearance: default, shopItemIds ?? new List<int> { 1, 2 });

        // ---- TryOpenShop ----

        [Fact]
        public void TryOpenShop_WithinRange_Succeeds()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000);
            Entity npc = SpawnNpc(world, 50000, 50000);

            (bool success, ShopActionFailedReason reason) = world.TryOpenShop(player.Id, npc.Id);

            Assert.True(success);
        }

        [Fact]
        public void TryOpenShop_UnknownNpc_ReturnsNpcNotFound()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000);

            (bool success, ShopActionFailedReason reason) = world.TryOpenShop(player.Id, npcEntityId: 999);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.NpcNotFound, reason);
        }

        [Fact]
        public void TryOpenShop_TargetIsPlayerNotNpc_ReturnsNpcNotFound()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000);
            Entity otherPlayer = JoinPlayer(world, 50000, 50000, characterId: 2);

            (bool success, ShopActionFailedReason reason) = world.TryOpenShop(player.Id, otherPlayer.Id);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.NpcNotFound, reason);
        }

        [Fact]
        public void TryOpenShop_NpcWithoutSortiment_ReturnsNotAShop()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000);
            Entity npc = SpawnNpc(world, 50000, 50000, shopItemIds: System.Array.Empty<int>());

            (bool success, ShopActionFailedReason reason) = world.TryOpenShop(player.Id, npc.Id);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.NotAShop, reason);
        }

        [Fact]
        public void TryOpenShop_OutOfRange_Fails()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000);
            Entity npc = SpawnNpc(world, 50000 + GameConstants.NpcInteractRangeWire + 1, 50000);

            (bool success, ShopActionFailedReason reason) = world.TryOpenShop(player.Id, npc.Id);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.OutOfRange, reason);
        }

        [Fact]
        public void TryOpenShop_ExactlyAtRange_Succeeds()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000);
            Entity npc = SpawnNpc(world, 50000 + GameConstants.NpcInteractRangeWire, 50000); // Grenzfall, inklusiv.

            (bool success, _) = world.TryOpenShop(player.Id, npc.Id);

            Assert.True(success);
        }

        // ---- TryBuy ----

        [Fact]
        public void TryBuy_EnoughYangAndFreeSlot_DeductsYangAndAddsItemToFirstFreeSlot()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000, yang: 500);
            Entity npc = SpawnNpc(world, 50000, 50000);

            (bool success, ShopActionFailedReason reason) = world.TryBuy(player.Id, npc.Id, itemId: 1);

            Assert.True(success);
            Assert.Equal(300, player.Yang); // 500 - 200 (BuyPrice)
            Assert.Equal(1, player.Inventory[0]);
        }

        [Fact]
        public void TryBuy_NotEnoughYang_Fails()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000, yang: 100); // < 200 BuyPrice
            Entity npc = SpawnNpc(world, 50000, 50000);

            (bool success, ShopActionFailedReason reason) = world.TryBuy(player.Id, npc.Id, itemId: 1);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.NotEnoughYang, reason);
            Assert.Equal(100, player.Yang); // unveraendert
        }

        [Fact]
        public void TryBuy_InventoryFull_Fails()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000, yang: 10_000);
            for (int i = 0; i < player.Inventory.Length; i++)
                player.Inventory[i] = 2; // Inventar komplett fuellen.
            Entity npc = SpawnNpc(world, 50000, 50000);

            (bool success, ShopActionFailedReason reason) = world.TryBuy(player.Id, npc.Id, itemId: 1);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.InventoryFull, reason);
            Assert.Equal(10_000, player.Yang); // unveraendert, kein Teilkauf.
        }

        [Fact]
        public void TryBuy_ItemNotInNpcSortiment_ReturnsItemNotInStock()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000, yang: 10_000);
            Entity npc = SpawnNpc(world, 50000, 50000); // Sortiment [1, 2], nicht 3.

            (bool success, ShopActionFailedReason reason) = world.TryBuy(player.Id, npc.Id, itemId: 3);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.ItemNotInStock, reason);
        }

        [Fact]
        public void TryBuy_OutOfRange_Fails()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000, yang: 10_000);
            Entity npc = SpawnNpc(world, 50000 + GameConstants.NpcInteractRangeWire + 1, 50000);

            (bool success, ShopActionFailedReason reason) = world.TryBuy(player.Id, npc.Id, itemId: 1);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.OutOfRange, reason);
        }

        // ---- TrySell ----

        [Fact]
        public void TrySell_ItemWithSellPrice_ClearsSlotAndCreditsYang()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000, yang: 100);
            player.Inventory[0] = 1; // Schwert, SellPrice 80.
            Entity npc = SpawnNpc(world, 50000, 50000);

            (bool success, ShopActionFailedReason reason) = world.TrySell(player.Id, npc.Id, inventorySlotIndex: 0);

            Assert.True(success);
            Assert.Equal(0, player.Inventory[0]);
            Assert.Equal(180, player.Yang); // 100 + 80
        }

        [Fact]
        public void TrySell_ItemWithoutSellPrice_ReturnsItemNotSellable()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000);
            player.Inventory[0] = 2; // Trank, SellPrice 0.
            Entity npc = SpawnNpc(world, 50000, 50000);

            (bool success, ShopActionFailedReason reason) = world.TrySell(player.Id, npc.Id, inventorySlotIndex: 0);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.ItemNotSellable, reason);
            Assert.Equal(2, player.Inventory[0]); // unveraendert.
        }

        [Fact]
        public void TrySell_EmptySlot_ReturnsEmptySlot()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000);
            Entity npc = SpawnNpc(world, 50000, 50000);

            (bool success, ShopActionFailedReason reason) = world.TrySell(player.Id, npc.Id, inventorySlotIndex: 0);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.EmptySlot, reason);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(20)]
        public void TrySell_InvalidSlotIndex_ReturnsInvalidSlot(int slotIndex)
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000);
            Entity npc = SpawnNpc(world, 50000, 50000);

            (bool success, ShopActionFailedReason reason) = world.TrySell(player.Id, npc.Id, slotIndex);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.InvalidSlot, reason);
        }

        [Fact]
        public void TrySell_ExceedingMaxYangCap_ReturnsYangCapReachedAndKeepsSlotOccupied()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000, yang: GameConstants.MaxYang - 10); // nur 10 Spielraum, SellPrice ist 80.
            player.Inventory[0] = 1;
            Entity npc = SpawnNpc(world, 50000, 50000);

            (bool success, ShopActionFailedReason reason) = world.TrySell(player.Id, npc.Id, inventorySlotIndex: 0);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.YangCapReached, reason);
            Assert.Equal(1, player.Inventory[0]); // kein Teilverkauf, Item bleibt im Slot.
            Assert.Equal(GameConstants.MaxYang - 10, player.Yang); // unveraendert.
        }

        [Fact]
        public void TrySell_OutOfRange_Fails()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000);
            player.Inventory[0] = 1;
            Entity npc = SpawnNpc(world, 50000 + GameConstants.NpcInteractRangeWire + 1, 50000);

            (bool success, ShopActionFailedReason reason) = world.TrySell(player.Id, npc.Id, inventorySlotIndex: 0);

            Assert.False(success);
            Assert.Equal(ShopActionFailedReason.OutOfRange, reason);
            Assert.Equal(1, player.Inventory[0]); // unveraendert.
        }

        // ---- CreditYang (0c: gemeinsame Methode mit MaxYang-Deckel) ----

        [Fact]
        public void CreditYang_WithinCap_AddsAmountAndReturnsTrue()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000, yang: 100);

            bool result = world.CreditYang(player.Id, 50);

            Assert.True(result);
            Assert.Equal(150, player.Yang);
        }

        [Fact]
        public void CreditYang_ExceedingCap_ReturnsFalseAndLeavesYangUnchanged()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000, yang: GameConstants.MaxYang - 10);

            bool result = world.CreditYang(player.Id, 50);

            Assert.False(result);
            Assert.Equal(GameConstants.MaxYang - 10, player.Yang);
        }

        [Fact]
        public void CreditYang_ExactlyAtCap_Succeeds()
        {
            World world = CreateWorld();
            Entity player = JoinPlayer(world, 50000, 50000, yang: GameConstants.MaxYang - 50);

            bool result = world.CreditYang(player.Id, 50);

            Assert.True(result);
            Assert.Equal(GameConstants.MaxYang, player.Yang);
        }

        [Fact]
        public void CreditYang_UnknownEntity_ReturnsFalse()
        {
            World world = CreateWorld();
            bool result = world.CreditYang(entityId: 999, amount: 50);
            Assert.False(result);
        }
    }
}
