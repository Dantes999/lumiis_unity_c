namespace Lumiis.Shared.Net
{
    /// <summary>Erste Nachricht des Clients nach dem Verbinden. Ab Protocol.Version 3: Token statt Name -
    /// der Server validiert das Token lokal (Lumiis.Data.TokenService, kein HTTP-Aufruf zu Auth) und lädt
    /// den Charakter per CharacterId + der im Token enthaltenen AccountId (Eigentümerschaft geprüft).</summary>
    public sealed class JoinRequestPacket : IPacket
    {
        public int ProtocolVersion;
        public string Token;
        public int CharacterId;

        public PacketId Id
        {
            get { return PacketId.JoinRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(ProtocolVersion);
            writer.WriteString(Token);
            writer.WriteInt32(CharacterId);
        }

        public void Read(PacketReader reader)
        {
            ProtocolVersion = reader.ReadInt32();
            Token = reader.ReadString();
            CharacterId = reader.ReadInt32();
        }
    }

    /// <summary>Der Client will zu einem Zielpunkt laufen (Weltkoordinaten in 1/100 Einheiten).</summary>
    public sealed class MoveToPacket : IPacket
    {
        public int X;
        public int Y;

        public PacketId Id
        {
            get { return PacketId.MoveTo; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(X);
            writer.WriteInt32(Y);
        }

        public void Read(PacketReader reader)
        {
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
        }
    }

    /// <summary>Beendet eine laufende Bewegung sofort (Taste losgelassen). Ohne dieses Paket würde der Server
    /// noch bis zum zuletzt gesendeten MoveTo-Ziel weiterlaufen. Kein Payload.</summary>
    public sealed class StopMovePacket : IPacket
    {
        public PacketId Id
        {
            get { return PacketId.StopMove; }
        }

        public void Write(PacketWriter writer)
        {
        }

        public void Read(PacketReader reader)
        {
        }
    }

    /// <summary>Der Client hebt einen Boden-Drop auf. Der Server prüft Existenz, Reichweite und Inventarplatz.</summary>
    public sealed class PickupItemRequestPacket : IPacket
    {
        public int GroundItemEntityId;

        public PacketId Id
        {
            get { return PacketId.PickupItemRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(GroundItemEntityId);
        }

        public void Read(PacketReader reader)
        {
            GroundItemEntityId = reader.ReadInt32();
        }
    }

    /// <summary>Gruppen-UI: lädt einen Spieler per Name ein (ersetzt den Chat-Befehl).</summary>
    public sealed class PartyInviteRequestPacket : IPacket
    {
        public string TargetName = "";

        public PacketId Id
        {
            get { return PacketId.PartyInviteRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteString(TargetName);
        }

        public void Read(PacketReader reader)
        {
            TargetName = reader.ReadString();
        }
    }

    /// <summary>Gruppen-UI: nimmt eine offene Einladung an oder lehnt sie ab.</summary>
    public sealed class PartyRespondRequestPacket : IPacket
    {
        public bool Accept;

        public PacketId Id
        {
            get { return PacketId.PartyRespondRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)(Accept ? 1 : 0));
        }

        public void Read(PacketReader reader)
        {
            Accept = reader.ReadByte() != 0;
        }
    }

    /// <summary>Gruppen-UI: verlässt die aktuelle Gruppe.</summary>
    public sealed class PartyLeaveRequestPacket : IPacket
    {
        public PacketId Id
        {
            get { return PacketId.PartyLeaveRequest; }
        }

        public void Write(PacketWriter writer)
        {
        }

        public void Read(PacketReader reader)
        {
        }
    }

    /// <summary>Gruppen-UI: fordert den vollen Gruppenstand an (Resync).</summary>
    public sealed class PartySnapshotRequestPacket : IPacket
    {
        public PacketId Id
        {
            get { return PacketId.PartySnapshotRequest; }
        }

        public void Write(PacketWriter writer)
        {
        }

        public void Read(PacketReader reader)
        {
        }
    }

    /// <summary>Gruppen-UI: der Leiter entfernt ein Mitglied (auch ein gerade offline gegangenes) per
    /// Charaktername. Der Server prüft Leiterschaft und Mitgliedschaft.</summary>
    public sealed class PartyKickRequestPacket : IPacket
    {
        public string TargetName = "";

        public PacketId Id
        {
            get { return PacketId.PartyKickRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteString(TargetName);
        }

        public void Read(PacketReader reader)
        {
            TargetName = reader.ReadString();
        }
    }

    /// <summary>Gruppen-UI: der Leiter übergibt die Leitung an ein Mitglied, das online ist.</summary>
    public sealed class PartyPromoteRequestPacket : IPacket
    {
        public string TargetName = "";

        public PacketId Id
        {
            get { return PacketId.PartyPromoteRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteString(TargetName);
        }

        public void Read(PacketReader reader)
        {
            TargetName = reader.ReadString();
        }
    }

    /// <summary>Gruppen-UI: der Leiter weist einem Mitglied eine Rolle zu (PartyRole.None entfernt sie).
    /// Unbekannte Rollenwerte werden vom Server abgelehnt, nicht getrennt (wie AllocateStatPoint).</summary>
    public sealed class PartySetRoleRequestPacket : IPacket
    {
        public string TargetName = "";
        public PartyRole Role;

        public PacketId Id
        {
            get { return PacketId.PartySetRoleRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteString(TargetName);
            writer.WriteByte((byte)Role);
        }

        public void Read(PacketReader reader)
        {
            TargetName = reader.ReadString();
            Role = (PartyRole)reader.ReadByte();
        }
    }

    /// <summary>Der Client greift eine sichtbare Entity an (M4a). Der Server prüft alles serverautoritativ
    /// erneut (Existenz, Leben, PvP, Cooldown, Reichweite) - siehe World.TryAttack.</summary>
    public sealed class AttackRequestPacket : IPacket
    {
        public int TargetEntityId;

        public PacketId Id
        {
            get { return PacketId.AttackRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(TargetEntityId);
        }

        public void Read(PacketReader reader)
        {
            TargetEntityId = reader.ReadInt32();
        }
    }

    /// <summary>Dev-Tool (siehe CLAUDE.md "Dev-Tools"): spawnt ein Monster nahe der Absenderposition. Nur
    /// wirksam, wenn der Server mit --allow-debug-commands gestartet wurde - sonst still verworfen.</summary>
    public sealed class DebugSpawnMonsterRequestPacket : IPacket
    {
        public int MobId;

        public PacketId Id
        {
            get { return PacketId.DebugSpawnMonsterRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(MobId);
        }

        public void Read(PacketReader reader)
        {
            MobId = reader.ReadInt32();
        }
    }

    /// <summary>Welcher der vier Ausrüstungs-Slots (M5a) - siehe UnequipItemRequestPacket.</summary>
    public enum EquipmentSlot : byte
    {
        Armor = 1,
        Helmet = 2,
        Weapon = 3,
        Shield = 4
    }

    /// <summary>Der Client will das Item aus dem angegebenen Inventarplatz anlegen (M5a). Der Server prüft
    /// alles serverautoritativ erneut (Slot belegt, passender ItemType) - siehe World.TryEquip.</summary>
    public sealed class EquipItemRequestPacket : IPacket
    {
        public int InventorySlotIndex;

        public PacketId Id
        {
            get { return PacketId.EquipItemRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(InventorySlotIndex);
        }

        public void Read(PacketReader reader)
        {
            InventorySlotIndex = reader.ReadInt32();
        }
    }

    /// <summary>Der Client will das Item aus dem angegebenen Ausrüstungs-Slot ablegen (M5a) - landet auf der
    /// ersten freien Inventarstelle, siehe World.TryUnequip.</summary>
    public sealed class UnequipItemRequestPacket : IPacket
    {
        public EquipmentSlot EquipmentSlot;

        public PacketId Id
        {
            get { return PacketId.UnequipItemRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)EquipmentSlot);
        }

        public void Read(PacketReader reader)
        {
            EquipmentSlot = (EquipmentSlot)reader.ReadByte();
        }
    }

    /// <summary>Der Client will das Item aus dem angegebenen Inventarplatz benutzen (M5a, z. B. Heiltrank) -
    /// siehe World.TryUseConsumable.</summary>
    public sealed class UseConsumableRequestPacket : IPacket
    {
        public int InventorySlotIndex;

        public PacketId Id
        {
            get { return PacketId.UseConsumableRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(InventorySlotIndex);
        }

        public void Read(PacketReader reader)
        {
            InventorySlotIndex = reader.ReadInt32();
        }
    }

    /// <summary>M6c: Der Client will die angelegte Ausrüstung im genannten Slot aufwerten. Der Server prüft
    /// alles erneut (Slot belegt, Maximalstufe, Schleifstein vorhanden, ggf. Bewahrungsrolle vorhanden) und
    /// verbraucht erst nach erfolgreicher Vorprüfung Material - siehe World.TryUpgrade.</summary>
    public sealed class UpgradeItemRequestPacket : IPacket
    {
        public EquipmentSlot EquipmentSlot;
        public bool UseProtectionScroll;

        public PacketId Id
        {
            get { return PacketId.UpgradeItemRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)EquipmentSlot);
            writer.WriteByte((byte)(UseProtectionScroll ? 1 : 0));
        }

        public void Read(PacketReader reader)
        {
            EquipmentSlot = (EquipmentSlot)reader.ReadByte();
            UseProtectionScroll = reader.ReadByte() != 0;
        }
    }

    /// <summary>Rechtsklick auf einen sichtbaren NPC (M5b) - der Server prüft Existenz/Kind/Reichweite
    /// erneut, siehe World.TryOpenShop.</summary>
    public sealed class OpenShopRequestPacket : IPacket
    {
        public int NpcEntityId;

        public PacketId Id
        {
            get { return PacketId.OpenShopRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(NpcEntityId);
        }

        public void Read(PacketReader reader)
        {
            NpcEntityId = reader.ReadInt32();
        }
    }

    /// <summary>Kauf-Knopf im Shop-Panel (M5b) - siehe World.TryBuy.</summary>
    public sealed class BuyItemRequestPacket : IPacket
    {
        public int NpcEntityId;
        public int ItemId;

        public PacketId Id
        {
            get { return PacketId.BuyItemRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(NpcEntityId);
            writer.WriteInt32(ItemId);
        }

        public void Read(PacketReader reader)
        {
            NpcEntityId = reader.ReadInt32();
            ItemId = reader.ReadInt32();
        }
    }

    /// <summary>Verkaufen-Knopf im Shop-Panel (M5b) - siehe World.TrySell.</summary>
    public sealed class SellItemRequestPacket : IPacket
    {
        public int NpcEntityId;
        public int InventorySlotIndex;

        public PacketId Id
        {
            get { return PacketId.SellItemRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(NpcEntityId);
            writer.WriteInt32(InventorySlotIndex);
        }

        public void Read(PacketReader reader)
        {
            NpcEntityId = reader.ReadInt32();
            InventorySlotIndex = reader.ReadInt32();
        }
    }

    /// <summary>Dev-Tool (siehe CLAUDE.md "Dev-Tools"): schreibt Amount über die gemeinsame Yang-Methode
    /// (World.CreditYang, MaxYang-Deckel) gut. Nur wirksam mit --allow-debug-commands, sonst still
    /// verworfen, analog zu DebugSpawnMonsterRequestPacket.</summary>
    public sealed class DebugGiveYangRequestPacket : IPacket
    {
        public long Amount;

        public PacketId Id
        {
            get { return PacketId.DebugGiveYangRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt64(Amount);
        }

        public void Read(PacketReader reader)
        {
            Amount = reader.ReadInt64();
        }
    }

    /// <summary>Einer der vier verteilbaren Werte (M6a, siehe CLAUDE.md Abschnitt "Fortschritt: Level, XP,
    /// Werte") - siehe AllocateStatPointRequestPacket.</summary>
    public enum StatKind : byte
    {
        St = 1,
        Ht = 2,
        Dx = 3,
        Iq = 4
    }

    /// <summary>Der Client will einen Statuspunkt auf den genannten Wert verteilen (M6a) - der Server prüft
    /// erneut, ob UnspentStatPoints > 0 ist (siehe World.TryAllocateStatPoint). Eine ungültige Anfrage
    /// (keine Punkte, unbekannter Wert) führt zu keiner Trennung, nur zu einem erneuten
    /// CharacterStatsPacket mit dem unveränderten Stand (siehe GameServer.HandleAllocateStatPointRequest).</summary>
    public sealed class AllocateStatPointRequestPacket : IPacket
    {
        public StatKind Stat;

        public PacketId Id
        {
            get { return PacketId.AllocateStatPointRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Stat);
        }

        public void Read(PacketReader reader)
        {
            Stat = (StatKind)reader.ReadByte();
        }
    }

    /// <summary>Dev-Tool (siehe CLAUDE.md "Dev-Tools"): setzt Level = Level, Xp/Spent* zurückgesetzt,
    /// Unspent* entsprechend nachgezählt (dient auch als Stat-Reset zum Testen, siehe
    /// World.ApplyDebugLevel). Nur wirksam mit --allow-debug-commands, sonst still verworfen, analog zu
    /// DebugSpawnMonsterRequestPacket/DebugGiveYangRequestPacket.</summary>
    public sealed class DebugSetLevelRequestPacket : IPacket
    {
        public int Level;

        public PacketId Id
        {
            get { return PacketId.DebugSetLevelRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(Level);
        }

        public void Read(PacketReader reader)
        {
            Level = reader.ReadInt32();
        }
    }

    /// <summary>Skill-Typ (M6b, siehe CLAUDE.md Abschnitt "Skills") - bestimmt, wie World.TryUseSkill einen
    /// Skill auflöst: SingleStrike/AreaAttack brauchen ResolveHit, AttackBuff/AttackSpeedBuff setzen nur
    /// einen Buff-Zustand.</summary>
    public enum SkillType : byte
    {
        SingleStrike = 1,
        AreaAttack = 2,
        AttackBuff = 3,
        AttackSpeedBuff = 4
    }

    /// <summary>Aktiver Buff-Typ (M6b) - Untermenge von SkillType (nur die beiden Buff-Skills haben einen
    /// aktiven Zustand mit Restzeit, siehe Entity.ActiveBuffs/ActiveBuffsChangedPacket).</summary>
    public enum BuffType : byte
    {
        AttackBuff = 1,
        AttackSpeedBuff = 2
    }

    /// <summary>Taste 1-4 im Spiel (M6b, echte Spielsteuerung, siehe CLAUDE.md "Skills") - der Server prüft
    /// alles serverautoritativ erneut (gelernt, SP, Skill-Cooldown, ggf. Ziel/Reichweite, siehe
    /// World.TryUseSkill). TargetEntityId 0 = kein Ziel/Selbstziel (AreaAttack/Buff-Skills brauchen keins).</summary>
    public sealed class UseSkillRequestPacket : IPacket
    {
        public int SkillId;
        public int TargetEntityId;

        public PacketId Id
        {
            get { return PacketId.UseSkillRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(SkillId);
            writer.WriteInt32(TargetEntityId);
        }

        public void Read(PacketReader reader)
        {
            SkillId = reader.ReadInt32();
            TargetEntityId = reader.ReadInt32();
        }
    }

    /// <summary>Der Client will einen Fertigkeitspunkt auf den genannten Skill verteilen (M6b) - der Server
    /// prüft erneut, ob UnspentSkillPoints > 0 UND die aktuelle Stufe &lt; 17 ist (siehe
    /// World.TryAllocateSkillPoint). Ungültige Anfrage (keine Punkte, unbekannter Skill, schon Stufe 17)
    /// führt zu keiner Trennung, nur zu einem erneuten CharacterStatsPacket mit dem unveränderten Stand
    /// (analog zu AllocateStatPointRequestPacket, siehe GameServer.HandleAllocateSkillPointRequest).</summary>
    public sealed class AllocateSkillPointRequestPacket : IPacket
    {
        public int SkillId;

        public PacketId Id
        {
            get { return PacketId.AllocateSkillPointRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(SkillId);
        }

        public void Read(PacketReader reader)
        {
            SkillId = reader.ReadInt32();
        }
    }

    /// <summary>M7-Chat: Client->Server. Text wird serverseitig getrimmt/limitiert, SenderName kommt aus der
    /// Entity (nicht aus dem Paket), Whisper benötigt TargetName. Rate-Limit im GameServer.</summary>
    public sealed class ChatRequestPacket : IPacket
    {
        public ChatChannel Channel;
        public string TargetName = "";
        public string Text = "";

        public PacketId Id
        {
            get { return PacketId.ChatRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Channel);
            writer.WriteString(TargetName);
            writer.WriteString(Text);
        }

        public void Read(PacketReader reader)
        {
            Channel = (ChatChannel)reader.ReadByte();
            TargetName = reader.ReadString();
            Text = reader.ReadString();
        }
    }

    /// <summary>Multi-Map (Protocol.Version 19): nur wirksam mit --allow-debug-commands (sonst still
    /// verworfen, siehe DebugSpawnMonsterRequestPacket). Versetzt den eigenen Charakter auf die
    /// Startposition der Zielkarte - Debug-API zum Testen des Kartenwechsels ohne Portal.</summary>
    public sealed class DebugChangeMapRequestPacket : IPacket
    {
        public string MapId = "";

        public PacketId Id
        {
            get { return PacketId.DebugChangeMapRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteString(MapId);
        }

        public void Read(PacketReader reader)
        {
            MapId = reader.ReadString();
        }
    }
}
