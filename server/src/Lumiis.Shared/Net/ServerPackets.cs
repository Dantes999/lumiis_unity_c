using System.Collections.Generic;

namespace Lumiis.Shared.Net
{
    public enum JoinRejectReason : byte
    {
        VersionMismatch = 1,
        InvalidToken = 2,
        TokenExpired = 3,
        CharacterNotFound = 4,
        CharacterAlreadyOnline = 5,
        ServerFull = 6
    }

    /// <summary>Unterscheidet Spieler- von Monster- (seit M3c) und NPC-Entities (seit M5b) in EntitySpawn
    /// (siehe dort).</summary>
    public enum EntityKind : byte
    {
        Player = 0,
        Monster = 1,
        Npc = 2
    }

    /// <summary>Der Client wurde aufgenommen und an der genannten Position gespawnt. Appearance ist die eigene
    /// Figur - der Client berechnet sie nie selbst, siehe CLAUDE.md-Konvention.</summary>
    public sealed class JoinAcceptedPacket : IPacket
    {
        public int EntityId;
        public int X;
        public int Y;
        public Appearance Appearance;

        public PacketId Id
        {
            get { return PacketId.JoinAccepted; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            writer.WriteInt32(X);
            writer.WriteInt32(Y);
            Appearance.Write(writer);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
            Appearance.Read(reader);
        }
    }

    /// <summary>Der Join wurde abgelehnt; die Verbindung wird danach getrennt.</summary>
    public sealed class JoinRejectedPacket : IPacket
    {
        public JoinRejectReason Reason;

        public PacketId Id
        {
            get { return PacketId.JoinRejected; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Reason);
        }

        public void Read(PacketReader reader)
        {
            Reason = (JoinRejectReason)reader.ReadByte();
        }
    }

    /// <summary>Eine (neue oder bereits vorhandene) Entity erscheint für den Client - eine andere Figur, ein
    /// Monster (seit M3c) oder ein NPC (seit M5b), öffentliche Daten inklusive Appearance. Bei Kind=Monster
    /// ist Appearance mit neutralen/Default-Werten befüllt (keine Bedeutung, das Feld bleibt aber für alle
    /// Kinds im Paket, damit der Client nicht mehrere verschiedene Paketformen unterscheiden muss). TypeId
    /// (bis M5a "MobId" genannt, seit M5b umbenannt - reiner Rename, keine Wire-Format-Änderung, siehe
    /// docs/tasks/M5b-shop.md Abschnitt 0b) zeigt je nach Kind auf data/mobs.json (Monster) oder data/npcs.json
    /// (Npc); bei Kind=Player ungenutzt/0.</summary>
    public sealed class EntitySpawnPacket : IPacket
    {
        public int EntityId;
        public string Name;
        public int X;
        public int Y;
        public Appearance Appearance;
        public EntityKind Kind;
        public int TypeId;

        public PacketId Id
        {
            get { return PacketId.EntitySpawn; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            writer.WriteString(Name);
            writer.WriteInt32(X);
            writer.WriteInt32(Y);
            Appearance.Write(writer);
            writer.WriteByte((byte)Kind);
            writer.WriteInt32(TypeId);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            Name = reader.ReadString();
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
            Appearance.Read(reader);
            Kind = (EntityKind)reader.ReadByte();
            TypeId = reader.ReadInt32();
        }
    }

    /// <summary>Eine Entity verschwindet (Disconnect o. ä.).</summary>
    public sealed class EntityDespawnPacket : IPacket
    {
        public int EntityId;

        public PacketId Id
        {
            get { return PacketId.EntityDespawn; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
        }
    }

    /// <summary>Eine Entity bewegt sich von Start- zu Zielposition; der Client interpoliert selbst (keine Positions-Broadcasts pro Tick).</summary>
    public sealed class EntityMovePacket : IPacket
    {
        public int EntityId;
        public int StartX;
        public int StartY;
        public int TargetX;
        public int TargetY;
        public int Speed;
        public uint ServerTick;

        public PacketId Id
        {
            get { return PacketId.EntityMove; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            writer.WriteInt32(StartX);
            writer.WriteInt32(StartY);
            writer.WriteInt32(TargetX);
            writer.WriteInt32(TargetY);
            writer.WriteInt32(Speed);
            writer.WriteUInt32(ServerTick);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            StartX = reader.ReadInt32();
            StartY = reader.ReadInt32();
            TargetX = reader.ReadInt32();
            TargetY = reader.ReadInt32();
            Speed = reader.ReadInt32();
            ServerTick = reader.ReadUInt32();
        }
    }

    /// <summary>Eine Entity ist exakt an der genannten Position zum Stillstand gekommen.</summary>
    public sealed class EntityStopPacket : IPacket
    {
        public int EntityId;
        public int X;
        public int Y;

        public PacketId Id
        {
            get { return PacketId.EntityStop; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            writer.WriteInt32(X);
            writer.WriteInt32(Y);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
        }
    }

    /// <summary>Grund, warum ein AttackRequestPacket abgelehnt wurde - siehe AttackFailedPacket/World.TryAttack.</summary>
    public enum AttackFailedReason : byte
    {
        TargetNotFound = 1,
        TargetAlreadyDead = 2,
        OutOfRange = 3,
        OnCooldown = 4,
        TargetIsPlayer = 5,

        /// <summary>M5b: Ziel ist ein NPC (Kind=Npc) - NPCs sind unangreifbar/statisch, siehe CLAUDE.md
        /// "Shop und NPCs".</summary>
        TargetNotAttackable = 6
    }

    /// <summary>Eine Entity wurde getroffen (M4a) - an alle AOI-Beobachter des Ziels (inklusive Angreifer,
    /// siehe GameServer.SendToVisible).</summary>
    public sealed class EntityDamagedPacket : IPacket
    {
        public int AttackerEntityId;
        public int TargetEntityId;
        public int Damage;
        public int TargetHpAfter;
        public int TargetMaxHp;

        public PacketId Id
        {
            get { return PacketId.EntityDamaged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(AttackerEntityId);
            writer.WriteInt32(TargetEntityId);
            writer.WriteInt32(Damage);
            writer.WriteInt32(TargetHpAfter);
            writer.WriteInt32(TargetMaxHp);
        }

        public void Read(PacketReader reader)
        {
            AttackerEntityId = reader.ReadInt32();
            TargetEntityId = reader.ReadInt32();
            Damage = reader.ReadInt32();
            TargetHpAfter = reader.ReadInt32();
            TargetMaxHp = reader.ReadInt32();
        }
    }

    /// <summary>Eine Entity ist gestorben (M4a) - an alle AOI-Beobachter. Bei Monstern folgt danach sofort
    /// ein EntityDespawnPacket (kein Respawn in M4a), bei Spielern nicht (siehe World-Klassenkommentar zum
    /// Spielertod - die Entity bleibt in World, nur CurrentHp bleibt 0 bis zum Respawn).</summary>
    public sealed class EntityDiedPacket : IPacket
    {
        public int EntityId;

        public PacketId Id
        {
            get { return PacketId.EntityDied; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
        }
    }

    /// <summary>Ein AttackRequestPacket wurde abgelehnt - nur an den Angreifer.</summary>
    public sealed class AttackFailedPacket : IPacket
    {
        public AttackFailedReason Reason;

        public PacketId Id
        {
            get { return PacketId.AttackFailed; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Reason);
        }

        public void Read(PacketReader reader)
        {
            Reason = (AttackFailedReason)reader.ReadByte();
        }
    }

    /// <summary>Der aktuelle Yang-Gesamtbetrag hat sich geändert (M4a) - nur an den Empfänger.</summary>
    public sealed class YangChangedPacket : IPacket
    {
        public long NewTotal;

        public PacketId Id
        {
            get { return PacketId.YangChanged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt64(NewTotal);
        }

        public void Read(PacketReader reader)
        {
            NewTotal = reader.ReadInt64();
        }
    }

    /// <summary>Voller Inventar-/Ausrüstungs-Zustand (M5a) - nur an den Empfänger, bei Join und nach jeder
    /// Änderung neu verschickt (voller Resync statt Delta, das Inventar ist mit maximal 20 Plätzen klein
    /// genug dafür). Slots enthält nur belegte Plätze (SlotIndex, ItemId) - ein fehlender SlotIndex bedeutet
    /// leer, genau wie 0 bei den vier Equipped-Feldern.</summary>
    public sealed class InventorySyncPacket : IPacket
    {
        public List<(int SlotIndex, int ItemId)> Slots = new List<(int SlotIndex, int ItemId)>();
        public int EquippedArmorItemId;
        public int EquippedHelmetItemId;
        public int EquippedWeaponItemId;
        public int EquippedShieldItemId;

        public PacketId Id
        {
            get { return PacketId.InventorySync; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Slots.Count);
            foreach ((int slotIndex, int itemId) in Slots)
            {
                writer.WriteByte((byte)slotIndex);
                writer.WriteInt32(itemId);
            }
            writer.WriteInt32(EquippedArmorItemId);
            writer.WriteInt32(EquippedHelmetItemId);
            writer.WriteInt32(EquippedWeaponItemId);
            writer.WriteInt32(EquippedShieldItemId);
        }

        public void Read(PacketReader reader)
        {
            int count = reader.ReadByte();
            Slots = new List<(int SlotIndex, int ItemId)>(count);
            for (int i = 0; i < count; i++)
            {
                int slotIndex = reader.ReadByte();
                int itemId = reader.ReadInt32();
                Slots.Add((slotIndex, itemId));
            }
            EquippedArmorItemId = reader.ReadInt32();
            EquippedHelmetItemId = reader.ReadInt32();
            EquippedWeaponItemId = reader.ReadInt32();
            EquippedShieldItemId = reader.ReadInt32();
        }
    }

    /// <summary>Grund, warum ein Inventar-/Ausrüstungs-Request abgelehnt wurde (M5a) - siehe
    /// InventoryActionFailedPacket/World.TryEquip/TryUnequip/TryUseConsumable. InventoryFull wird zusätzlich
    /// (als reine Info, kein harter Fehler) verschickt, wenn ein Monster-Drop mangels freiem Platz verloren
    /// geht - siehe World.TryAttack.</summary>
    public enum InventoryActionFailedReason : byte
    {
        InvalidSlot = 1,
        EmptySlot = 2,
        WrongItemType = 3,
        NotConsumable = 4,
        InventoryFull = 5,
        TargetAtFullHp = 6
    }

    /// <summary>Ein Inventar-/Ausrüstungs-Request wurde abgelehnt - nur an den Anfragenden.</summary>
    public sealed class InventoryActionFailedPacket : IPacket
    {
        public InventoryActionFailedReason Reason;

        public PacketId Id
        {
            get { return PacketId.InventoryActionFailed; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Reason);
        }

        public void Read(PacketReader reader)
        {
            Reason = (InventoryActionFailedReason)reader.ReadByte();
        }
    }

    /// <summary>Das Aussehen einer Entity hat sich geändert (M5a, Equip/Unequip) - an alle AOI-Beobachter
    /// inklusive der Entity selbst, siehe PacketId.AppearanceChanged-Kommentar.</summary>
    public sealed class AppearanceChangedPacket : IPacket
    {
        public int EntityId;
        public Appearance Appearance;

        public PacketId Id
        {
            get { return PacketId.AppearanceChanged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            Appearance.Write(writer);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            Appearance.Read(reader);
        }
    }

    /// <summary>Das Leben einer Entity hat sich außerhalb eines Treffers geändert (Bugfix nach M5a) - an alle
    /// AOI-Beobachter inklusive der Entity selbst, siehe PacketId.EntityHpChanged-Kommentar für die genauen
    /// Auslöser. EntityDamagedPacket bleibt für den Schadensfall unverändert (trägt zusätzlich Angreifer/
    /// Schaden) - dieses Paket deckt die Fälle ab, in denen es keinen Angreifer/Schadenswert gibt.</summary>
    public sealed class EntityHpChangedPacket : IPacket
    {
        public int EntityId;
        public int CurrentHp;
        public int MaxHp;

        public PacketId Id
        {
            get { return PacketId.EntityHpChanged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            writer.WriteInt32(CurrentHp);
            writer.WriteInt32(MaxHp);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            CurrentHp = reader.ReadInt32();
            MaxHp = reader.ReadInt32();
        }
    }

    /// <summary>Antwort auf OpenShopRequest (M5b, nur an den Anfragenden) - das Sortiment des NPCs
    /// (ItemId, BuyPrice je Eintrag, direkt aus NpcDefinition.ShopItemIds + ItemCatalog aufgelöst, siehe
    /// World.TryOpenShop). NpcEntityId erlaubt dem Client, das Panel dem richtigen NPC zuzuordnen (z. B. bei
    /// schnell aufeinanderfolgenden Requests).</summary>
    public sealed class ShopContentPacket : IPacket
    {
        public int NpcEntityId;
        public List<(int ItemId, long BuyPrice)> Items = new List<(int, long)>();

        public PacketId Id
        {
            get { return PacketId.ShopContent; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(NpcEntityId);
            writer.WriteByte((byte)Items.Count);
            foreach ((int itemId, long buyPrice) in Items)
            {
                writer.WriteInt32(itemId);
                writer.WriteInt64(buyPrice);
            }
        }

        public void Read(PacketReader reader)
        {
            NpcEntityId = reader.ReadInt32();
            int count = reader.ReadByte();
            Items = new List<(int, long)>(count);
            for (int i = 0; i < count; i++)
            {
                int itemId = reader.ReadInt32();
                long buyPrice = reader.ReadInt64();
                Items.Add((itemId, buyPrice));
            }
        }
    }

    /// <summary>Grund, warum ein Shop-Request (Open/Buy/Sell) abgelehnt wurde (M5b) - siehe
    /// World.TryOpenShop/TryBuy/TrySell.</summary>
    public enum ShopActionFailedReason : byte
    {
        NpcNotFound = 1,
        NotAShop = 2,
        OutOfRange = 3,
        ItemNotInStock = 4,
        NotEnoughYang = 5,
        InventoryFull = 6,
        InvalidSlot = 7,
        EmptySlot = 8,
        ItemNotSellable = 9,
        YangCapReached = 10
    }

    /// <summary>Ein Shop-Request wurde abgelehnt - nur an den Anfragenden, Verbindung bleibt bestehen.</summary>
    public sealed class ShopActionFailedPacket : IPacket
    {
        public ShopActionFailedReason Reason;

        public PacketId Id
        {
            get { return PacketId.ShopActionFailed; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Reason);
        }

        public void Read(PacketReader reader)
        {
            Reason = (ShopActionFailedReason)reader.ReadByte();
        }
    }

    /// <summary>Der vollständige eigene Fortschritts-/Kampfwert-Stand (M6a, nur an den Besitzer, siehe
    /// CLAUDE.md Abschnitt "Fortschritt: Level, XP, Werte") - immer ein voller Resync, kein Delta (analog zu
    /// InventorySyncPacket). St/Ht/Dx/Iq sind jeweils die Summe aus Klassen-Basiswert und verteilten Punkten
    /// (World.RecomputeDerivedStats). XpToNext ist 0 bei MaxLevel (kein weiterer XP-Gewinn möglich).
    /// Attack/Defense/MaxHp/CurrentHp/MaxSp/CurrentSp sind dieselben abgeleiteten Werte wie überall sonst im
    /// Protokoll (EntityDamaged/EntityHpChanged lesen/liefern weiterhin nur CurrentHp/MaxHp, kein
    /// Sonderfall).</summary>
    public sealed class CharacterStatsPacket : IPacket
    {
        public int Level;
        public long Xp;
        public long XpToNext;
        public int St;
        public int Ht;
        public int Dx;
        public int Iq;
        public int UnspentStatPoints;
        public int UnspentSkillPoints;
        public int MaxHp;
        public int CurrentHp;
        public int MaxSp;
        public int CurrentSp;
        public int Attack;
        public int Defense;
        public int DodgeBp;

        public PacketId Id
        {
            get { return PacketId.CharacterStats; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(Level);
            writer.WriteInt64(Xp);
            writer.WriteInt64(XpToNext);
            writer.WriteInt32(St);
            writer.WriteInt32(Ht);
            writer.WriteInt32(Dx);
            writer.WriteInt32(Iq);
            writer.WriteInt32(UnspentStatPoints);
            writer.WriteInt32(UnspentSkillPoints);
            writer.WriteInt32(MaxHp);
            writer.WriteInt32(CurrentHp);
            writer.WriteInt32(MaxSp);
            writer.WriteInt32(CurrentSp);
            writer.WriteInt32(Attack);
            writer.WriteInt32(Defense);
            writer.WriteInt32(DodgeBp);
        }

        public void Read(PacketReader reader)
        {
            Level = reader.ReadInt32();
            Xp = reader.ReadInt64();
            XpToNext = reader.ReadInt64();
            St = reader.ReadInt32();
            Ht = reader.ReadInt32();
            Dx = reader.ReadInt32();
            Iq = reader.ReadInt32();
            UnspentStatPoints = reader.ReadInt32();
            UnspentSkillPoints = reader.ReadInt32();
            MaxHp = reader.ReadInt32();
            CurrentHp = reader.ReadInt32();
            MaxSp = reader.ReadInt32();
            CurrentSp = reader.ReadInt32();
            Attack = reader.ReadInt32();
            Defense = reader.ReadInt32();
            DodgeBp = reader.ReadInt32();
        }
    }

    /// <summary>Ein Angriff wurde ausgewichen (M6a, siehe World.TryAttack/CLAUDE.md "Kampf") - an alle
    /// AOI-Beobachter des Ziels (inklusive Angreifer, gleicher Verteiler wie EntityDamaged - der Angreifer
    /// ist dank Nahkampfreichweite &lt; AOI-Radius immer selbst dabei). Ersetzt EntityDamagedPacket für
    /// diesen Treffer komplett (kein Schaden, kein Tod), der Cooldown des Angreifers wird trotzdem verbraucht
    /// (siehe World.TryAttack - unverändert).</summary>
    public sealed class EntityDodgedPacket : IPacket
    {
        public int AttackerEntityId;
        public int TargetEntityId;

        public PacketId Id
        {
            get { return PacketId.EntityDodged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(AttackerEntityId);
            writer.WriteInt32(TargetEntityId);
        }

        public void Read(PacketReader reader)
        {
            AttackerEntityId = reader.ReadInt32();
            TargetEntityId = reader.ReadInt32();
        }
    }
}
