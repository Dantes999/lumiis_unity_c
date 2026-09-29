using System;
using System.Collections.Generic;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Ergebnis von World.TryAttack - bei Erfolg alle Daten, die GameServer für EntityDamaged/EntityDied/
/// EntityDodged/YangChanged/CharacterStats/Drops braucht (siehe GameServer.HandleAttackRequest), bei
/// Fehlschlag der Grund (spiegelt AttackFailedReason 1:1, siehe Shared).</summary>
public readonly struct AttackResult
{
    public bool Success { get; }

    /// <summary>Nur bei !Success aussagekräftig.</summary>
    public AttackFailedReason Reason { get; }

    /// <summary>M6a: true, wenn das Ziel ausgewichen ist (siehe World.TryAttack) - dann sind Damage/
    /// TargetHpAfter/TargetDied/YangAwarded/Drops bedeutungslos (0/false/leer), GameServer verschickt
    /// EntityDodgedPacket statt EntityDamagedPacket.</summary>
    public bool Dodged { get; }

    public int Damage { get; }
    public int TargetHpAfter { get; }
    public int TargetMaxHp { get; }
    public bool TargetDied { get; }
    public EntityKind TargetKind { get; }

    /// <summary>> 0 nur, wenn das Ziel ein Monster war und dabei gestorben ist.</summary>
    public long YangAwarded { get; }

    /// <summary>M6a: > 0 nur, wenn der Angreifer ein Spieler war, der dabei ein Monster getötet hat, UND
    /// noch nicht auf ProgressionCatalog.MaxLevel steht (siehe World.AwardXp) - GameServer verschickt dann
    /// ein neues CharacterStatsPacket an den Angreifer.</summary>
    public long XpAwarded { get; }

    /// <summary>M6a: true, wenn XpAwarded mindestens einen Levelaufstieg ausgelöst hat - GameServer speichert
    /// den Fortschritt dann sofort statt nur periodisch (siehe docs/tasks/M6a-progression.md Abschnitt 5).</summary>
    public bool LeveledUp { get; }

    /// <summary>M5a: ItemIds, die beim Tod des Ziels erfolgreich ins Inventar des Angreifers gelegt wurden
    /// (leer, wenn kein Drop gewürfelt wurde oder das Ziel kein Monster war/nicht starb).</summary>
    public IReadOnlyList<int> DroppedItemIds { get; }

    /// <summary>M5a: true, wenn mindestens ein gewürfelter Drop wegen vollem Inventar verloren ging (siehe
    /// World.TryAttack) - der Kill selbst bleibt trotzdem gültig, nur ein Info-Paket an den Angreifer.</summary>
    public bool ItemLostToFullInventory { get; }

    private AttackResult(bool success, AttackFailedReason reason, bool dodged, int damage, int targetHpAfter, int targetMaxHp,
        bool targetDied, EntityKind targetKind, long yangAwarded, long xpAwarded, bool leveledUp,
        IReadOnlyList<int> droppedItemIds, bool itemLostToFullInventory)
    {
        Success = success;
        Reason = reason;
        Dodged = dodged;
        Damage = damage;
        TargetHpAfter = targetHpAfter;
        TargetMaxHp = targetMaxHp;
        TargetDied = targetDied;
        TargetKind = targetKind;
        YangAwarded = yangAwarded;
        XpAwarded = xpAwarded;
        LeveledUp = leveledUp;
        DroppedItemIds = droppedItemIds;
        ItemLostToFullInventory = itemLostToFullInventory;
    }

    public static AttackResult Fail(AttackFailedReason reason) =>
        new(false, reason, dodged: false, 0, 0, 0, false, default, 0, 0, false, Array.Empty<int>(), false);

    public static AttackResult Dodge(EntityKind targetKind) =>
        new(true, default, dodged: true, 0, 0, 0, targetDied: false, targetKind, yangAwarded: 0, xpAwarded: 0, leveledUp: false,
            Array.Empty<int>(), false);

    public static AttackResult Hit(int damage, int targetHpAfter, int targetMaxHp, bool targetDied, EntityKind targetKind, long yangAwarded,
        long xpAwarded = 0, bool leveledUp = false, IReadOnlyList<int>? droppedItemIds = null, bool itemLostToFullInventory = false) =>
        new(true, default, dodged: false, damage, targetHpAfter, targetMaxHp, targetDied, targetKind, yangAwarded, xpAwarded, leveledUp,
            droppedItemIds ?? Array.Empty<int>(), itemLostToFullInventory);
}
