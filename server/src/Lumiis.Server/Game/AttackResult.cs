using System;
using System.Collections.Generic;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Ergebnis von World.TryAttack - bei Erfolg alle Daten, die GameServer für EntityDamaged/EntityDied/
/// YangChanged/Drops braucht (siehe GameServer.HandleAttackRequest), bei Fehlschlag der Grund (spiegelt
/// AttackFailedReason 1:1, siehe Shared).</summary>
public readonly struct AttackResult
{
    public bool Success { get; }

    /// <summary>Nur bei !Success aussagekräftig.</summary>
    public AttackFailedReason Reason { get; }

    public int Damage { get; }
    public int TargetHpAfter { get; }
    public int TargetMaxHp { get; }
    public bool TargetDied { get; }
    public EntityKind TargetKind { get; }

    /// <summary>> 0 nur, wenn das Ziel ein Monster war und dabei gestorben ist.</summary>
    public long YangAwarded { get; }

    /// <summary>M5a: ItemIds, die beim Tod des Ziels erfolgreich ins Inventar des Angreifers gelegt wurden
    /// (leer, wenn kein Drop gewürfelt wurde oder das Ziel kein Monster war/nicht starb).</summary>
    public IReadOnlyList<int> DroppedItemIds { get; }

    /// <summary>M5a: true, wenn mindestens ein gewürfelter Drop wegen vollem Inventar verloren ging (siehe
    /// World.TryAttack) - der Kill selbst bleibt trotzdem gültig, nur ein Info-Paket an den Angreifer.</summary>
    public bool ItemLostToFullInventory { get; }

    private AttackResult(bool success, AttackFailedReason reason, int damage, int targetHpAfter, int targetMaxHp,
        bool targetDied, EntityKind targetKind, long yangAwarded, IReadOnlyList<int> droppedItemIds, bool itemLostToFullInventory)
    {
        Success = success;
        Reason = reason;
        Damage = damage;
        TargetHpAfter = targetHpAfter;
        TargetMaxHp = targetMaxHp;
        TargetDied = targetDied;
        TargetKind = targetKind;
        YangAwarded = yangAwarded;
        DroppedItemIds = droppedItemIds;
        ItemLostToFullInventory = itemLostToFullInventory;
    }

    public static AttackResult Fail(AttackFailedReason reason) =>
        new(false, reason, 0, 0, 0, false, default, 0, Array.Empty<int>(), false);

    public static AttackResult Hit(int damage, int targetHpAfter, int targetMaxHp, bool targetDied, EntityKind targetKind, long yangAwarded,
        IReadOnlyList<int>? droppedItemIds = null, bool itemLostToFullInventory = false) =>
        new(true, default, damage, targetHpAfter, targetMaxHp, targetDied, targetKind, yangAwarded,
            droppedItemIds ?? Array.Empty<int>(), itemLostToFullInventory);
}
