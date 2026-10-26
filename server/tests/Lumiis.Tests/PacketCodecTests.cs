using System;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests
{
    public class PacketCodecTests
    {
        [Fact]
        public void JoinRequest_RoundTrip()
        {
            var original = new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = "abc.def", CharacterId = 42 };
            var decoded = Assert.IsType<JoinRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
            Assert.Equal(original.Token, decoded.Token);
            Assert.Equal(original.CharacterId, decoded.CharacterId);
        }

        [Fact]
        public void MoveTo_RoundTrip()
        {
            var original = new MoveToPacket { X = -12345, Y = 987654 };
            var decoded = Assert.IsType<MoveToPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(-12345, decoded.X);
            Assert.Equal(987654, decoded.Y);
        }

        [Fact]
        public void PartyRequests_RoundTrip()
        {
            var invite = new PartyInviteRequestPacket { TargetName = "Bob" };
            var inviteDecoded = Assert.IsType<PartyInviteRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(invite)));
            Assert.Equal("Bob", inviteDecoded.TargetName);

            var respond = new PartyRespondRequestPacket { Accept = true };
            var respondDecoded = Assert.IsType<PartyRespondRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(respond)));
            Assert.True(respondDecoded.Accept);

            Assert.IsType<PartyLeaveRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(new PartyLeaveRequestPacket())));
            Assert.IsType<PartySnapshotRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(new PartySnapshotRequestPacket())));

            var kick = new PartyKickRequestPacket { TargetName = "Carol" };
            Assert.Equal("Carol", Assert.IsType<PartyKickRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(kick))).TargetName);

            var promote = new PartyPromoteRequestPacket { TargetName = "Dave" };
            Assert.Equal("Dave", Assert.IsType<PartyPromoteRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(promote))).TargetName);

            var setRole = new PartySetRoleRequestPacket { TargetName = "Eve", Role = PartyRole.Defender };
            var setRoleDecoded = Assert.IsType<PartySetRoleRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(setRole)));
            Assert.Equal("Eve", setRoleDecoded.TargetName);
            Assert.Equal(PartyRole.Defender, setRoleDecoded.Role);

            var inviteReceived = new PartyInviteReceivedPacket { InviterName = "Alice" };
            var inviteReceivedDecoded = Assert.IsType<PartyInviteReceivedPacket>(PacketCodec.Decode(PacketCodec.Encode(inviteReceived)));
            Assert.Equal("Alice", inviteReceivedDecoded.InviterName);
        }

        [Fact]
        public void StopMove_RoundTrip()
        {
            var original = new StopMovePacket();
            var decoded = Assert.IsType<StopMovePacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(PacketId.StopMove, decoded.Id);
        }

        [Fact]
        public void JoinAccepted_RoundTrip()
        {
            var appearance = new Appearance { BodyId = 1, HairStyleId = 2, HairColorId = 3, ArmorId = 4, HelmetId = 0, WeaponId = 1, ShieldId = 0 };
            var original = new JoinAcceptedPacket
            {
                EntityId = 42, X = 50000, Y = 50000, Appearance = appearance,
                MapId = "greenfield", MapName = "Gruenes Feld", MapWidth = 100000, MapHeight = 100000
            };
            var decoded = Assert.IsType<JoinAcceptedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.X, decoded.X);
            Assert.Equal(original.Y, decoded.Y);
            Assert.Equal(original.Appearance, decoded.Appearance);
            Assert.Equal("greenfield", decoded.MapId);
            Assert.Equal("Gruenes Feld", decoded.MapName);
            Assert.Equal(100000, decoded.MapWidth);
            Assert.Equal(100000, decoded.MapHeight);
        }

        [Fact]
        public void MapChanged_RoundTrip()
        {
            var original = new MapChangedPacket
            {
                EntityId = 7, X = 50000, Y = 65000,
                MapId = "greenfield", MapName = "Gruenes Feld", MapWidth = 100000, MapHeight = 100000
            };
            var decoded = Assert.IsType<MapChangedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.X, decoded.X);
            Assert.Equal(original.Y, decoded.Y);
            Assert.Equal(original.MapId, decoded.MapId);
            Assert.Equal(original.MapName, decoded.MapName);
            Assert.Equal(original.MapWidth, decoded.MapWidth);
            Assert.Equal(original.MapHeight, decoded.MapHeight);
        }

        [Fact]
        public void MapEvents_RoundTrip()
        {
            var original = new MapEventsPacket();
            original.Events.Add(new MapEventInfo { EventId = 3, MobId = 41, Name = "Hauerkönig", PositionX = 51234, PositionY = 47000 });
            original.Events.Add(new MapEventInfo { EventId = 5, MobId = 44, Name = "Schneetiger", PositionX = 1, PositionY = 99999 });
            var decoded = Assert.IsType<MapEventsPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(2, decoded.Events.Count);
            Assert.Equal(original.Events[0].EventId, decoded.Events[0].EventId);
            Assert.Equal(original.Events[0].MobId, decoded.Events[0].MobId);
            Assert.Equal(original.Events[0].Name, decoded.Events[0].Name);
            Assert.Equal(original.Events[1].PositionX, decoded.Events[1].PositionX);
            Assert.Equal(original.Events[1].PositionY, decoded.Events[1].PositionY);

            var empty = Assert.IsType<MapEventsPacket>(PacketCodec.Decode(PacketCodec.Encode(new MapEventsPacket())));
            Assert.Empty(empty.Events);
        }

        [Fact]
        public void DebugChangeMapRequest_RoundTrip()
        {
            var original = new DebugChangeMapRequestPacket { MapId = "village" };
            var decoded = Assert.IsType<DebugChangeMapRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal("village", decoded.MapId);
        }

        [Fact]
        public void JoinRejected_RoundTrip()
        {
            var original = new JoinRejectedPacket { Reason = JoinRejectReason.CharacterAlreadyOnline };
            var decoded = Assert.IsType<JoinRejectedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Reason, decoded.Reason);
        }

        [Fact]
        public void EntitySpawn_RoundTrip_Player()
        {
            var appearance = new Appearance { BodyId = 1, HairStyleId = 3, HairColorId = 5, ArmorId = 2, HelmetId = 2, WeaponId = 0, ShieldId = 1 };
            var original = new EntitySpawnPacket { EntityId = 7, Name = "Held_1", X = 100, Y = -200, Appearance = appearance, Kind = EntityKind.Player, TypeId = 0 };
            var decoded = Assert.IsType<EntitySpawnPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.Name, decoded.Name);
            Assert.Equal(original.X, decoded.X);
            Assert.Equal(original.Y, decoded.Y);
            Assert.Equal(original.Appearance, decoded.Appearance);
            Assert.Equal(EntityKind.Player, decoded.Kind);
            Assert.Equal(0, decoded.TypeId);
        }

        [Fact]
        public void EntitySpawn_RoundTrip_Monster()
        {
            var original = new EntitySpawnPacket { EntityId = 12, Name = "Wildhund", X = 30000, Y = 30000, Appearance = default, Kind = EntityKind.Monster, TypeId = 1, Level = 23 };
            var decoded = Assert.IsType<EntitySpawnPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(23, decoded.Level);
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.Name, decoded.Name);
            Assert.Equal(original.X, decoded.X);
            Assert.Equal(original.Y, decoded.Y);
            Assert.Equal(EntityKind.Monster, decoded.Kind);
            Assert.Equal(1, decoded.TypeId);
        }

        [Fact]
        public void EntityDespawn_RoundTrip()
        {
            var original = new EntityDespawnPacket { EntityId = 7 };
            var decoded = Assert.IsType<EntityDespawnPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
        }

        [Fact]
        public void EntityMove_RoundTrip()
        {
            var original = new EntityMovePacket
            {
                EntityId = 3,
                StartX = 1000,
                StartY = 2000,
                TargetX = 5000,
                TargetY = 6000,
                Speed = 500,
                ServerTick = 123456u
            };
            var decoded = Assert.IsType<EntityMovePacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.StartX, decoded.StartX);
            Assert.Equal(original.StartY, decoded.StartY);
            Assert.Equal(original.TargetX, decoded.TargetX);
            Assert.Equal(original.TargetY, decoded.TargetY);
            Assert.Equal(original.Speed, decoded.Speed);
            Assert.Equal(original.ServerTick, decoded.ServerTick);
        }

        [Fact]
        public void EntityStop_RoundTrip()
        {
            var original = new EntityStopPacket { EntityId = 3, X = 5000, Y = 6000 };
            var decoded = Assert.IsType<EntityStopPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.X, decoded.X);
            Assert.Equal(original.Y, decoded.Y);
        }

        [Fact]
        public void AttackRequest_RoundTrip()
        {
            var original = new AttackRequestPacket { TargetEntityId = 9, AimX = -707, AimY = 707 };
            var decoded = Assert.IsType<AttackRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.TargetEntityId, decoded.TargetEntityId);
            Assert.Equal(original.AimX, decoded.AimX);
            Assert.Equal(original.AimY, decoded.AimY);
        }

        [Fact]
        public void EntityDamaged_RoundTrip()
        {
            var original = new EntityDamagedPacket { AttackerEntityId = 1, TargetEntityId = 2, Damage = 12, TargetHpAfter = 18, TargetMaxHp = 30 };
            var decoded = Assert.IsType<EntityDamagedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.AttackerEntityId, decoded.AttackerEntityId);
            Assert.Equal(original.TargetEntityId, decoded.TargetEntityId);
            Assert.Equal(original.Damage, decoded.Damage);
            Assert.Equal(original.TargetHpAfter, decoded.TargetHpAfter);
            Assert.Equal(original.TargetMaxHp, decoded.TargetMaxHp);
        }

        [Fact]
        public void EntityDied_RoundTrip()
        {
            var original = new EntityDiedPacket { EntityId = 5 };
            var decoded = Assert.IsType<EntityDiedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
        }

        [Fact]
        public void AttackFailed_RoundTrip()
        {
            var original = new AttackFailedPacket { Reason = AttackFailedReason.OutOfRange };
            var decoded = Assert.IsType<AttackFailedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Reason, decoded.Reason);
        }

        [Fact]
        public void YangChanged_RoundTrip()
        {
            var original = new YangChangedPacket { NewTotal = 123456789L };
            var decoded = Assert.IsType<YangChangedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NewTotal, decoded.NewTotal);
        }

        [Fact]
        public void DebugSpawnMonsterRequest_RoundTrip()
        {
            var original = new DebugSpawnMonsterRequestPacket { MobId = 2 };
            var decoded = Assert.IsType<DebugSpawnMonsterRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.MobId, decoded.MobId);
        }

        [Fact]
        public void EquipItemRequest_RoundTrip()
        {
            var original = new EquipItemRequestPacket { InventorySlotIndex = 7 };
            var decoded = Assert.IsType<EquipItemRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.InventorySlotIndex, decoded.InventorySlotIndex);
        }

        [Fact]
        public void UnequipItemRequest_RoundTrip()
        {
            var original = new UnequipItemRequestPacket { EquipmentSlot = EquipmentSlot.Weapon };
            var decoded = Assert.IsType<UnequipItemRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EquipmentSlot, decoded.EquipmentSlot);
        }

        [Fact]
        public void UseConsumableRequest_RoundTrip()
        {
            var original = new UseConsumableRequestPacket { InventorySlotIndex = 3 };
            var decoded = Assert.IsType<UseConsumableRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.InventorySlotIndex, decoded.InventorySlotIndex);
        }

        [Fact]
        public void InventorySync_RoundTrip()
        {
            var original = new InventorySyncPacket
            {
                Slots = new System.Collections.Generic.List<(int SlotIndex, int ItemId, int UpgradeLevel, int Count)> { (0, 1, 3, 1), (5, 8, 0, 200), (19, 3, 9, 1) },
                EquippedArmorItemId = 4,
                EquippedHelmetItemId = 0,
                EquippedWeaponItemId = 1,
                EquippedShieldItemId = 7,
                EquippedArmorUpgradeLevel = 2,
                EquippedHelmetUpgradeLevel = 0,
                EquippedWeaponUpgradeLevel = 9,
                EquippedShieldUpgradeLevel = 1
            };
            var decoded = Assert.IsType<InventorySyncPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Slots, decoded.Slots);
            Assert.Equal(original.EquippedArmorItemId, decoded.EquippedArmorItemId);
            Assert.Equal(original.EquippedHelmetItemId, decoded.EquippedHelmetItemId);
            Assert.Equal(original.EquippedWeaponItemId, decoded.EquippedWeaponItemId);
            Assert.Equal(original.EquippedShieldItemId, decoded.EquippedShieldItemId);
            Assert.Equal(original.EquippedArmorUpgradeLevel, decoded.EquippedArmorUpgradeLevel);
            Assert.Equal(original.EquippedHelmetUpgradeLevel, decoded.EquippedHelmetUpgradeLevel);
            Assert.Equal(original.EquippedWeaponUpgradeLevel, decoded.EquippedWeaponUpgradeLevel);
            Assert.Equal(original.EquippedShieldUpgradeLevel, decoded.EquippedShieldUpgradeLevel);
        }

        [Fact]
        public void InventorySync_RoundTrip_EmptySlots()
        {
            var original = new InventorySyncPacket { EquippedWeaponItemId = 1 };
            var decoded = Assert.IsType<InventorySyncPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Empty(decoded.Slots);
            Assert.Equal(1, decoded.EquippedWeaponItemId);
        }

        [Fact]
        public void InventoryActionFailed_RoundTrip()
        {
            var original = new InventoryActionFailedPacket { Reason = InventoryActionFailedReason.InventoryFull };
            var decoded = Assert.IsType<InventoryActionFailedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Reason, decoded.Reason);
        }

        [Fact]
        public void AppearanceChanged_RoundTrip()
        {
            var appearance = new Appearance { BodyId = 1, HairStyleId = 2, HairColorId = 3, ArmorId = 4, HelmetId = 0, WeaponId = 2, ShieldId = 7 };
            var original = new AppearanceChangedPacket { EntityId = 42, Appearance = appearance };
            var decoded = Assert.IsType<AppearanceChangedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.Appearance, decoded.Appearance);
        }

        [Fact]
        public void EntityHpChanged_RoundTrip()
        {
            var original = new EntityHpChangedPacket { EntityId = 42, CurrentHp = 70, MaxHp = 100 };
            var decoded = Assert.IsType<EntityHpChangedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.CurrentHp, decoded.CurrentHp);
            Assert.Equal(original.MaxHp, decoded.MaxHp);
        }

        [Fact]
        public void OpenShopRequest_RoundTrip()
        {
            var original = new OpenShopRequestPacket { NpcEntityId = 5 };
            var decoded = Assert.IsType<OpenShopRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NpcEntityId, decoded.NpcEntityId);
        }

        [Fact]
        public void BuyItemRequest_RoundTrip()
        {
            var original = new BuyItemRequestPacket { NpcEntityId = 5, ItemId = 3 };
            var decoded = Assert.IsType<BuyItemRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NpcEntityId, decoded.NpcEntityId);
            Assert.Equal(original.ItemId, decoded.ItemId);
        }

        [Fact]
        public void SellItemRequest_RoundTrip()
        {
            var original = new SellItemRequestPacket { NpcEntityId = 5, InventorySlotIndex = 7 };
            var decoded = Assert.IsType<SellItemRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NpcEntityId, decoded.NpcEntityId);
            Assert.Equal(original.InventorySlotIndex, decoded.InventorySlotIndex);
        }

        [Fact]
        public void DebugGiveYangRequest_RoundTrip()
        {
            var original = new DebugGiveYangRequestPacket { Amount = 1000 };
            var decoded = Assert.IsType<DebugGiveYangRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Amount, decoded.Amount);
        }

        [Fact]
        public void ShopContent_RoundTrip()
        {
            var original = new ShopContentPacket
            {
                NpcEntityId = 5,
                Items = new System.Collections.Generic.List<(int ItemId, long BuyPrice)> { (8, 20), (2, 200), (3, 350) }
            };
            var decoded = Assert.IsType<ShopContentPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NpcEntityId, decoded.NpcEntityId);
            Assert.Equal(original.Items, decoded.Items);
        }

        [Fact]
        public void ShopContent_RoundTrip_EmptyItems()
        {
            var original = new ShopContentPacket { NpcEntityId = 5 };
            var decoded = Assert.IsType<ShopContentPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Empty(decoded.Items);
        }

        [Fact]
        public void ShopActionFailed_RoundTrip()
        {
            var original = new ShopActionFailedPacket { Reason = ShopActionFailedReason.NotEnoughYang };
            var decoded = Assert.IsType<ShopActionFailedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Reason, decoded.Reason);
        }

        [Fact]
        public void EntitySpawn_RoundTrip_Npc()
        {
            var appearance = new Appearance { BodyId = 1, HairStyleId = 1, HairColorId = 1, ArmorId = 3, HelmetId = 0, WeaponId = 0, ShieldId = 0 };
            var original = new EntitySpawnPacket { EntityId = 20, Name = "Dorfhaendler", X = 50000, Y = 50500, Appearance = appearance, Kind = EntityKind.Npc, TypeId = 1 };
            var decoded = Assert.IsType<EntitySpawnPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(EntityKind.Npc, decoded.Kind);
            Assert.Equal(1, decoded.TypeId);
        }

        [Fact]
        public void AttackFailed_RoundTrip_TargetNotAttackable()
        {
            var original = new AttackFailedPacket { Reason = AttackFailedReason.TargetNotAttackable };
            var decoded = Assert.IsType<AttackFailedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(AttackFailedReason.TargetNotAttackable, decoded.Reason);
        }

        [Fact]
        public void AllocateStatPointRequest_RoundTrip()
        {
            var original = new AllocateStatPointRequestPacket { Stat = StatKind.Dx };
            var decoded = Assert.IsType<AllocateStatPointRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Stat, decoded.Stat);
        }

        [Fact]
        public void DebugSetLevelRequest_RoundTrip()
        {
            var original = new DebugSetLevelRequestPacket { Level = 12 };
            var decoded = Assert.IsType<DebugSetLevelRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Level, decoded.Level);
        }

        [Fact]
        public void CharacterStats_RoundTrip()
        {
            var original = new CharacterStatsPacket
            {
                Level = 5,
                Xp = 1234,
                XpToNext = 650,
                St = 8,
                Ht = 6,
                Dx = 5,
                Iq = 4,
                UnspentStatPoints = 3,
                UnspentSkillPoints = 1,
                MaxHp = 148,
                CurrentHp = 90,
                MaxSp = 46,
                CurrentSp = 20,
                Attack = 25,
                Defense = 8,
                DodgeBp = 250,
                Skills = new System.Collections.Generic.List<(int, int)> { (1, 5), (3, 12), (4, 17) }
            };
            var decoded = Assert.IsType<CharacterStatsPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Level, decoded.Level);
            Assert.Equal(original.Xp, decoded.Xp);
            Assert.Equal(original.XpToNext, decoded.XpToNext);
            Assert.Equal(original.St, decoded.St);
            Assert.Equal(original.Ht, decoded.Ht);
            Assert.Equal(original.Dx, decoded.Dx);
            Assert.Equal(original.Iq, decoded.Iq);
            Assert.Equal(original.UnspentStatPoints, decoded.UnspentStatPoints);
            Assert.Equal(original.UnspentSkillPoints, decoded.UnspentSkillPoints);
            Assert.Equal(original.MaxHp, decoded.MaxHp);
            Assert.Equal(original.CurrentHp, decoded.CurrentHp);
            Assert.Equal(original.MaxSp, decoded.MaxSp);
            Assert.Equal(original.CurrentSp, decoded.CurrentSp);
            Assert.Equal(original.Attack, decoded.Attack);
            Assert.Equal(original.Defense, decoded.Defense);
            Assert.Equal(original.DodgeBp, decoded.DodgeBp);
            Assert.Equal(original.Skills, decoded.Skills);
        }

        [Fact]
        public void CharacterStats_RoundTrip_EmptySkills()
        {
            var original = new CharacterStatsPacket { Level = 1 };
            var decoded = Assert.IsType<CharacterStatsPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Empty(decoded.Skills);
        }

        [Fact]
        public void EntityDodged_RoundTrip()
        {
            var original = new EntityDodgedPacket { AttackerEntityId = 3, TargetEntityId = 7 };
            var decoded = Assert.IsType<EntityDodgedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.AttackerEntityId, decoded.AttackerEntityId);
            Assert.Equal(original.TargetEntityId, decoded.TargetEntityId);
        }

        [Fact]
        public void UseSkillRequest_RoundTrip()
        {
            var original = new UseSkillRequestPacket { SkillId = 1, TargetEntityId = 42 };
            var decoded = Assert.IsType<UseSkillRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.SkillId, decoded.SkillId);
            Assert.Equal(original.TargetEntityId, decoded.TargetEntityId);
        }

        [Fact]
        public void AllocateSkillPointRequest_RoundTrip()
        {
            var original = new AllocateSkillPointRequestPacket { SkillId = 3 };
            var decoded = Assert.IsType<AllocateSkillPointRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.SkillId, decoded.SkillId);
        }

        [Fact]
        public void UseSkillFailed_RoundTrip()
        {
            var original = new UseSkillFailedPacket { Reason = UseSkillFailedReason.NotEnoughSp };
            var decoded = Assert.IsType<UseSkillFailedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Reason, decoded.Reason);
        }

        [Fact]
        public void ActiveBuffsChanged_RoundTrip()
        {
            var original = new ActiveBuffsChangedPacket
            {
                Buffs = new System.Collections.Generic.List<(BuffType, long)> { (BuffType.AttackBuff, 12345), (BuffType.AttackSpeedBuff, 6789) }
            };
            var decoded = Assert.IsType<ActiveBuffsChangedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Buffs, decoded.Buffs);
        }

        [Fact]
        public void ActiveBuffsChanged_RoundTrip_Empty()
        {
            var original = new ActiveBuffsChangedPacket();
            var decoded = Assert.IsType<ActiveBuffsChangedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Empty(decoded.Buffs);
        }

        [Fact]
        public void Decode_HonorsOffsetAndCount()
        {
            byte[] body = PacketCodec.Encode(new MoveToPacket { X = 1, Y = 2 });
            byte[] framed = new byte[body.Length + 3];
            Buffer.BlockCopy(body, 0, framed, 2, body.Length);

            var decoded = Assert.IsType<MoveToPacket>(PacketCodec.Decode(framed, 2, body.Length));
            Assert.Equal(1, decoded.X);
            Assert.Equal(2, decoded.Y);
        }

        [Fact]
        public void UnknownId_Throws()
        {
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(new byte[] { 0xFF, 0xFF }));
        }

        [Fact]
        public void NoneId_IsRejected()
        {
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(new byte[] { 0, 0 }));
        }

        [Fact]
        public void TrailingBytes_Throw()
        {
            byte[] data = PacketCodec.Encode(new MoveToPacket { X = 1, Y = 2 });
            Array.Resize(ref data, data.Length + 1);
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(data));
        }

        [Fact]
        public void EmptyData_Throws()
        {
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(new byte[0]));
        }

        [Fact]
        public void EveryPacketId_HasFactoryEntry()
        {
            foreach (PacketId id in Enum.GetValues(typeof(PacketId)))
            {
                if (id == PacketId.None) continue;
                IPacket packet = PacketFactory.Create(id);
                Assert.NotNull(packet);
                Assert.Equal(id, packet.Id);
            }
        }

        [Fact]
        public void ChatRequest_RoundTrip()
        {
            var original = new ChatRequestPacket { Channel = ChatChannel.Whisper, TargetName = "Bob", Text = "Hallo Bob!" };
            var decoded = Assert.IsType<ChatRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(ChatChannel.Whisper, decoded.Channel);
            Assert.Equal("Bob", decoded.TargetName);
            Assert.Equal("Hallo Bob!", decoded.Text);
        }

        [Fact]
        public void ChatMessage_RoundTrip()
        {
            var original = new ChatMessagePacket { Channel = ChatChannel.Global, SenderName = "Alice", Text = "Hallo Welt" };
            var decoded = Assert.IsType<ChatMessagePacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(ChatChannel.Global, decoded.Channel);
            Assert.Equal("Alice", decoded.SenderName);
            Assert.Equal("Hallo Welt", decoded.Text);
        }

        [Fact]
        public void PartyChanged_RoundTrip()
        {
            var original = new PartyChangedPacket { LeaderEntityId = 1 };
            original.Members.Add((1, "Alice", 12, 200, 240));
            original.Members.Add((2, "Bob", 10, 150, 200));
            var decoded = Assert.IsType<PartyChangedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(1, decoded.LeaderEntityId);
            Assert.Equal(2, decoded.Members.Count);
            Assert.Equal((1, "Alice", 12, 200, 240), decoded.Members[0]);
            Assert.Equal((2, "Bob", 10, 150, 200), decoded.Members[1]);
            // Ohne MemberStatus-Einträge gilt jedes Mitglied als online ohne Rolle.
            Assert.Equal((true, PartyRole.None), decoded.MemberStatus[0]);
            Assert.Equal((true, PartyRole.None), decoded.MemberStatus[1]);
        }

        [Fact]
        public void PartyChanged_WithOfflineMemberAndRoles_RoundTrip()
        {
            var original = new PartyChangedPacket { LeaderEntityId = 1 };
            original.Members.Add((1, "Alice", 12, 200, 240));
            original.MemberStatus.Add((true, PartyRole.Attacker));
            original.Members.Add((0, "Bob", 10, 150, 200));
            original.MemberStatus.Add((false, PartyRole.Defender));
            var decoded = Assert.IsType<PartyChangedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal((0, "Bob", 10, 150, 200), decoded.Members[1]);
            Assert.Equal((true, PartyRole.Attacker), decoded.MemberStatus[0]);
            Assert.Equal((false, PartyRole.Defender), decoded.MemberStatus[1]);
        }

        [Theory]
        [InlineData(EquipmentSlot.Armor, false)]
        [InlineData(EquipmentSlot.Weapon, true)]
        [InlineData(EquipmentSlot.Shield, true)]
        public void UpgradeItemRequest_RoundTrip(EquipmentSlot slot, bool useProtectionScroll)
        {
            var original = new UpgradeItemRequestPacket { EquipmentSlot = slot, UseProtectionScroll = useProtectionScroll };
            var decoded = Assert.IsType<UpgradeItemRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(slot, decoded.EquipmentSlot);
            Assert.Equal(useProtectionScroll, decoded.UseProtectionScroll);
        }

        [Theory]
        [InlineData(true, false, 3)]
        [InlineData(false, true, 0)]
        [InlineData(false, false, 5)]
        public void UpgradeItemResult_RoundTrip(bool success, bool destroyed, int newUpgradeLevel)
        {
            var original = new UpgradeItemResultPacket
            {
                EquipmentSlot = EquipmentSlot.Weapon,
                Success = success,
                Destroyed = destroyed,
                NewUpgradeLevel = newUpgradeLevel
            };
            var decoded = Assert.IsType<UpgradeItemResultPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(EquipmentSlot.Weapon, decoded.EquipmentSlot);
            Assert.Equal(success, decoded.Success);
            Assert.Equal(destroyed, decoded.Destroyed);
            Assert.Equal(newUpgradeLevel, decoded.NewUpgradeLevel);
        }

        [Theory]
        [InlineData(UpgradeActionFailedReason.SlotEmpty)]
        [InlineData(UpgradeActionFailedReason.AlreadyMaxLevel)]
        [InlineData(UpgradeActionFailedReason.NoMaterial)]
        [InlineData(UpgradeActionFailedReason.NoProtectionScroll)]
        public void UpgradeActionFailed_RoundTrip(UpgradeActionFailedReason reason)
        {
            var original = new UpgradeActionFailedPacket { Reason = reason };
            var decoded = Assert.IsType<UpgradeActionFailedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(reason, decoded.Reason);
        }
    }
}
