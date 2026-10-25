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
        Npc = 2,
        GroundItem = 3
    }

    /// <summary>Technischer NPC-Dienst für die UI (z. B. Shop oder Schmied). Wird mit EntitySpawn übertragen;
    /// die eigentliche Reichweiten-/Zustandsprüfung bleibt serverseitig.</summary>
    public enum NpcService : byte
    {
        None = 0,
        Merchant = 1,
        Blacksmith = 2
    }

    /// <summary>Der Client wurde aufgenommen und an der genannten Position gespawnt. Appearance ist die eigene
    /// Figur - der Client berechnet sie nie selbst, siehe CLAUDE.md-Konvention. Seit Protocol.Version 19
    /// zusätzlich die Karte (Id/Name/Maße), damit der Client Boden/Portale/Kartenname korrekt aufbaut.</summary>
    public sealed class JoinAcceptedPacket : IPacket
    {
        public int EntityId;
        public int X;
        public int Y;
        public Appearance Appearance;
        public string MapId = "village";
        public string MapName = "Dorf";
        public int MapWidth = 100000;
        public int MapHeight = 100000;

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
            writer.WriteString(MapId);
            writer.WriteString(MapName);
            writer.WriteInt32(MapWidth);
            writer.WriteInt32(MapHeight);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
            Appearance.Read(reader);
            MapId = reader.ReadString();
            MapName = reader.ReadString();
            MapWidth = reader.ReadInt32();
            MapHeight = reader.ReadInt32();
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

        /// <summary>Nur Kind=GroundItem: Aufwertungsstufe der Instanz (0-9).</summary>
        public byte ItemUpgradeLevel;

        /// <summary>Nur Kind=Npc: Dienst des NPCs (Shop/Schmied).</summary>
        public NpcService NpcService;

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
            writer.WriteByte(ItemUpgradeLevel);
            writer.WriteByte((byte)NpcService);
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
            ItemUpgradeLevel = reader.ReadByte();
            NpcService = (NpcService)reader.ReadByte();
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

    /// <summary>Voller Inventar-/Ausrüstungs-Zustand (M5a, seit M6c inklusive Aufwertungsstufen) - nur an den
    /// Empfänger, bei Join und nach jeder Änderung neu verschickt (voller Resync statt Delta, das Inventar ist
    /// mit maximal 20 Plätzen klein genug dafür). Slots enthält nur belegte Plätze (SlotIndex, ItemId,
    /// UpgradeLevel 0-9) - ein fehlender SlotIndex bedeutet leer, genau wie 0 bei den vier Equipped-Feldern.
    /// Die vier Equipped...UpgradeLevel gehören jeweils zur Equipped...ItemId desselben Slots.</summary>
    public sealed class InventorySyncPacket : IPacket
    {
        public List<(int SlotIndex, int ItemId, int UpgradeLevel)> Slots = new List<(int SlotIndex, int ItemId, int UpgradeLevel)>();
        public int EquippedArmorItemId;
        public int EquippedHelmetItemId;
        public int EquippedWeaponItemId;
        public int EquippedShieldItemId;
        public int EquippedArmorUpgradeLevel;
        public int EquippedHelmetUpgradeLevel;
        public int EquippedWeaponUpgradeLevel;
        public int EquippedShieldUpgradeLevel;

        public PacketId Id
        {
            get { return PacketId.InventorySync; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Slots.Count);
            foreach ((int slotIndex, int itemId, int upgradeLevel) in Slots)
            {
                writer.WriteByte((byte)slotIndex);
                writer.WriteInt32(itemId);
                writer.WriteByte((byte)upgradeLevel);
            }
            writer.WriteInt32(EquippedArmorItemId);
            writer.WriteInt32(EquippedHelmetItemId);
            writer.WriteInt32(EquippedWeaponItemId);
            writer.WriteInt32(EquippedShieldItemId);
            writer.WriteByte((byte)EquippedArmorUpgradeLevel);
            writer.WriteByte((byte)EquippedHelmetUpgradeLevel);
            writer.WriteByte((byte)EquippedWeaponUpgradeLevel);
            writer.WriteByte((byte)EquippedShieldUpgradeLevel);
        }

        public void Read(PacketReader reader)
        {
            int count = reader.ReadByte();
            Slots = new List<(int SlotIndex, int ItemId, int UpgradeLevel)>(count);
            for (int i = 0; i < count; i++)
            {
                int slotIndex = reader.ReadByte();
                int itemId = reader.ReadInt32();
                int upgradeLevel = reader.ReadByte();
                Slots.Add((slotIndex, itemId, upgradeLevel));
            }
            EquippedArmorItemId = reader.ReadInt32();
            EquippedHelmetItemId = reader.ReadInt32();
            EquippedWeaponItemId = reader.ReadInt32();
            EquippedShieldItemId = reader.ReadInt32();
            EquippedArmorUpgradeLevel = reader.ReadByte();
            EquippedHelmetUpgradeLevel = reader.ReadByte();
            EquippedWeaponUpgradeLevel = reader.ReadByte();
            EquippedShieldUpgradeLevel = reader.ReadByte();
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
        TargetAtFullHp = 6,
        GroundItemNotFound = 7,
        PickupOutOfRange = 8,
        PickupNotOwner = 9
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

    /// <summary>Grund, warum eine Aufwertung gar nicht erst versucht wurde (M6c) - bei allen Gründen wird
    /// NICHTS verbraucht. Siehe World.TryUpgrade/UpgradeActionFailedPacket.</summary>
    public enum UpgradeActionFailedReason : byte
    {
        SlotEmpty = 1,
        AlreadyMaxLevel = 2,
        NoMaterial = 3,
        NoProtectionScroll = 4,
        NoBlacksmith = 5,
        BlacksmithOutOfRange = 6
    }

    /// <summary>M6c: Ergebnis eines tatsächlich durchgeführten Aufwertungsversuchs (Schleifstein wurde
    /// verbraucht) - nur an den Anfragenden. Success=false + Destroyed=true: Item wurde zerstört, Slot ist
    /// jetzt leer (NewUpgradeLevel=0). Success=false + Destroyed=false: Fehlschlag MIT Bewahrungsrolle, Item
    /// bleibt auf NewUpgradeLevel. Success=true: Stufe ist jetzt NewUpgradeLevel.</summary>
    public sealed class UpgradeItemResultPacket : IPacket
    {
        public EquipmentSlot EquipmentSlot;
        public bool Success;
        public bool Destroyed;
        public int NewUpgradeLevel;

        public PacketId Id
        {
            get { return PacketId.UpgradeItemResult; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)EquipmentSlot);
            writer.WriteByte((byte)(Success ? 1 : 0));
            writer.WriteByte((byte)(Destroyed ? 1 : 0));
            writer.WriteByte((byte)NewUpgradeLevel);
        }

        public void Read(PacketReader reader)
        {
            EquipmentSlot = (EquipmentSlot)reader.ReadByte();
            Success = reader.ReadByte() != 0;
            Destroyed = reader.ReadByte() != 0;
            NewUpgradeLevel = reader.ReadByte();
        }
    }

    /// <summary>M6c: Vorprüfung fehlgeschlagen (leerer Slot, Maximalstufe, fehlendes Material/Schutzrolle) -
    /// kein Verbrauch, kein Zufallswurf, Verbindung bleibt bestehen. Nur an den Anfragenden.</summary>
    public sealed class UpgradeActionFailedPacket : IPacket
    {
        public UpgradeActionFailedReason Reason;

        public PacketId Id
        {
            get { return PacketId.UpgradeActionFailed; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Reason);
        }

        public void Read(PacketReader reader)
        {
            Reason = (UpgradeActionFailedReason)reader.ReadByte();
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

        /// <summary>M6b: alle gelernten Skills (SkillId, Level 1-17) - Skills auf Stufe 0 (nicht gelernt)
        /// fehlen in der Liste, analog zu InventorySyncPacket.Slots (kein Eintrag = leer). Vollständiger
        /// eigener Stand wie der Rest dieses Pakets, kein Delta.</summary>
        public List<(int SkillId, int Level)> Skills = new List<(int, int)>();

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
            writer.WriteByte((byte)Skills.Count);
            foreach ((int skillId, int level) in Skills)
            {
                writer.WriteInt32(skillId);
                writer.WriteByte((byte)level);
            }
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
            int skillCount = reader.ReadByte();
            Skills = new List<(int, int)>(skillCount);
            for (int i = 0; i < skillCount; i++)
            {
                int skillId = reader.ReadInt32();
                int level = reader.ReadByte();
                Skills.Add((skillId, level));
            }
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

    /// <summary>Grund, warum ein UseSkillRequestPacket abgelehnt wurde (M6b) - siehe World.TryUseSkill.</summary>
    public enum UseSkillFailedReason : byte
    {
        NotLearned = 1,
        NotEnoughSp = 2,
        OnCooldown = 3,
        TargetRequired = 4,
        TargetNotFound = 5,
        TargetAlreadyDead = 6,
        OutOfRange = 7,
        TargetIsPlayer = 8,
        TargetNotAttackable = 9
    }

    /// <summary>Ein UseSkillRequestPacket wurde abgelehnt - nur an den Anfragenden, Verbindung bleibt
    /// bestehen (analog zu AttackFailedPacket).</summary>
    public sealed class UseSkillFailedPacket : IPacket
    {
        public UseSkillFailedReason Reason;

        public PacketId Id
        {
            get { return PacketId.UseSkillFailed; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Reason);
        }

        public void Read(PacketReader reader)
        {
            Reason = (UseSkillFailedReason)reader.ReadByte();
        }
    }

    /// <summary>Voller eigener aktive-Buffs-Stand (M6b, nur an den Besitzer, siehe CLAUDE.md "Skills") - immer
    /// ein voller Resync (analog zu InventorySyncPacket), kein Delta. RemainingMs statt eines absoluten
    /// Zeitstempels (Projekt-Konvention: keine Client-Uhrensynchronisation) - der Client zählt lokal mit
    /// Time.deltaTime herunter. Leere Liste = keine aktiven Buffs. Verschickt bei jedem Skill-Einsatz, der
    /// den Buff-Zustand ändert, UND wenn ein Buff durch World.Tick abläuft.</summary>
    public sealed class ActiveBuffsChangedPacket : IPacket
    {
        public List<(BuffType Type, long RemainingMs)> Buffs = new List<(BuffType, long)>();

        public PacketId Id
        {
            get { return PacketId.ActiveBuffsChanged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Buffs.Count);
            foreach ((BuffType type, long remainingMs) in Buffs)
            {
                writer.WriteByte((byte)type);
                writer.WriteInt64(remainingMs);
            }
        }

        public void Read(PacketReader reader)
        {
            int count = reader.ReadByte();
            Buffs = new List<(BuffType, long)>(count);
            for (int i = 0; i < count; i++)
            {
                BuffType type = (BuffType)reader.ReadByte();
                long remainingMs = reader.ReadInt64();
                Buffs.Add((type, remainingMs));
            }
        }
    }

    /// <summary>M7-Chat: Kanal einer Chat-Nachricht. Map/Global sind aktuell dieselbe Broadcast-Gruppe
    /// (eine Karte), bleiben aber getrennt, damit später Karten-/Weltkanäle ohne Wire-Bruch möglich sind.
    /// System ist nur Server->Client. Whisper braucht einen Zielnamen.</summary>
    public enum ChatChannel : byte
    {
        Map = 0,
        Global = 1,
        Whisper = 2,
        System = 3
    }

    /// <summary>M7-Chat: eine Chat-Nachricht vom Server. SenderName kommt immer vom Server (Entity-Name), nie
    /// aus dem Client-Paket - der Client kann seinen Absendernamen nicht fälschen.</summary>
    public sealed class ChatMessagePacket : IPacket
    {
        public ChatChannel Channel;
        public string SenderName;
        public string Text;

        public PacketId Id
        {
            get { return PacketId.ChatMessage; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Channel);
            writer.WriteString(SenderName);
            writer.WriteString(Text);
        }

        public void Read(PacketReader reader)
        {
            Channel = (ChatChannel)reader.ReadByte();
            SenderName = reader.ReadString();
            Text = reader.ReadString();
        }
    }

    /// <summary>Gruppenrolle (Protocol.Version 20). Die Boni je Rolle stehen in data/party.json; der Client
    /// zeigt nur den Namen an.</summary>
    public enum PartyRole : byte
    {
        None = 0,
        Attacker = 1,
        Defender = 2
    }

    /// <summary>M7-Party: voller Gruppenstand (Resync, kein Delta). Members enthält Leader und Mitglieder
    /// in stabiler Reihenfolge; eine leere Liste bedeutet "keine Gruppe" (Panel ausblenden).</summary>
    public sealed class PartyChangedPacket : IPacket
    {
        public int LeaderEntityId;
        /// <summary>EntityId ist 0 bei einem Mitglied, das gerade offline ist (seit Protocol.Version 20
        /// bleiben Mitglieder beim Ausloggen in der Gruppe). Hp/MaxHp sind dann der letzte bekannte Stand.</summary>
        public List<(int EntityId, string Name, int Level, int Hp, int MaxHp)> Members = new List<(int, string, int, int, int)>();
        /// <summary>Parallel zu Members (gleiche Reihenfolge und Länge): Online-Status und Rolle. Als eigene
        /// Liste, damit bestehende Auswerter von Members unverändert weiter kompilieren.</summary>
        public List<(bool Online, PartyRole Role)> MemberStatus = new List<(bool, PartyRole)>();

        public PacketId Id
        {
            get { return PacketId.PartyChanged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(LeaderEntityId);
            writer.WriteByte((byte)Members.Count);
            for (int i = 0; i < Members.Count; i++)
            {
                (int entityId, string name, int level, int hp, int maxHp) = Members[i];
                writer.WriteInt32(entityId);
                writer.WriteString(name);
                writer.WriteInt32(level);
                writer.WriteInt32(hp);
                writer.WriteInt32(maxHp);
                // Fehlt ein Status-Eintrag, gilt das Mitglied als online ohne Rolle (Default für alte Aufrufer).
                bool online = i < MemberStatus.Count ? MemberStatus[i].Online : true;
                PartyRole role = i < MemberStatus.Count ? MemberStatus[i].Role : PartyRole.None;
                writer.WriteBool(online);
                writer.WriteByte((byte)role);
            }
        }

        public void Read(PacketReader reader)
        {
            LeaderEntityId = reader.ReadInt32();
            int count = reader.ReadByte();
            Members = new List<(int, string, int, int, int)>(count);
            MemberStatus = new List<(bool, PartyRole)>(count);
            for (int i = 0; i < count; i++)
            {
                int entityId = reader.ReadInt32();
                string name = reader.ReadString();
                int level = reader.ReadInt32();
                int hp = reader.ReadInt32();
                int maxHp = reader.ReadInt32();
                bool online = reader.ReadBool();
                PartyRole role = (PartyRole)reader.ReadByte();
                Members.Add((entityId, name, level, hp, maxHp));
                MemberStatus.Add((online, role));
            }
        }
    }

    /// <summary>Gruppen-UI: der eigene Spieler wurde eingeladen. Die UI kann darauf ein Annehmen/-
    /// Ablehnen-Dialog zeigen; `PartyRespondRequestPacket` beantwortet sie.</summary>
    public sealed class PartyInviteReceivedPacket : IPacket
    {
        public string InviterName = "";

        public PacketId Id
        {
            get { return PacketId.PartyInviteReceived; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteString(InviterName);
        }

        public void Read(PacketReader reader)
        {
            InviterName = reader.ReadString();
        }
    }

    /// <summary>Multi-Map (Protocol.Version 19): eigener Kartenwechsel. Enthält die neue Karte, ihre Maße und
    /// die bereits gesetzte Zielposition. Der Client verwirft alle Entities/Views, baut Boden/Portale neu und
    /// legt seine eigene Figur an der Zielposition an; fremde Entities der neuen Karte folgen über den
    /// normalen AOI-Diff (EntitySpawn).</summary>
    public sealed class MapChangedPacket : IPacket
    {
        public int EntityId;
        public int X;
        public int Y;
        public string MapId = "";
        public string MapName = "";
        public int MapWidth = 100000;
        public int MapHeight = 100000;

        public PacketId Id
        {
            get { return PacketId.MapChanged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            writer.WriteInt32(X);
            writer.WriteInt32(Y);
            writer.WriteString(MapId);
            writer.WriteString(MapName);
            writer.WriteInt32(MapWidth);
            writer.WriteInt32(MapHeight);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
            MapId = reader.ReadString();
            MapName = reader.ReadString();
            MapWidth = reader.ReadInt32();
            MapHeight = reader.ReadInt32();
        }
    }
}
