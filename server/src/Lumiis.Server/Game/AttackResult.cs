using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Ergebnis von World.TryAttack - bei Erfolg alle Daten, die GameServer für EntityDamaged/EntityDied/
/// YangChanged braucht (siehe GameServer.HandleAttackRequest), bei Fehlschlag der Grund (spiegelt
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

    private AttackResult(bool success, AttackFailedReason reason, int damage, int targetHpAfter, int targetMaxHp,
        bool targetDied, EntityKind targetKind, long yangAwarded)
    {
        Success = success;
        Reason = reason;
        Damage = damage;
        TargetHpAfter = targetHpAfter;
        TargetMaxHp = targetMaxHp;
        TargetDied = targetDied;
        TargetKind = targetKind;
        YangAwarded = yangAwarded;
    }

    public static AttackResult Fail(AttackFailedReason reason) =>
        new(false, reason, 0, 0, 0, false, default, 0);

    public static AttackResult Hit(int damage, int targetHpAfter, int targetMaxHp, bool targetDied, EntityKind targetKind, long yangAwarded) =>
        new(true, default, damage, targetHpAfter, targetMaxHp, targetDied, targetKind, yangAwarded);
}
