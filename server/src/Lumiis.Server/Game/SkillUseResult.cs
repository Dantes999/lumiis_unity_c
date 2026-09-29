using System;
using System.Collections.Generic;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Ergebnis von World.TryUseSkill (M6b) - bei Erfolg SkillType (damit GameServer weiß, welche
/// Pakete zu verschicken sind) und je nach Typ entweder Hits (SingleStrike: genau ein Eintrag, AreaAttack:
/// 0..n Einträge, jeder mit eigenem AttackResult inkl. Ausweichen/Tod/XP/Yang/Drops - siehe
/// World.ResolveHit) oder BuffApplied (AttackBuff/AttackSpeedBuff). Bei Fehlschlag nur Reason (spiegelt
/// UseSkillFailedReason 1:1, siehe Shared).</summary>
public readonly struct SkillUseResult
{
    public bool Success { get; }

    /// <summary>Nur bei !Success aussagekräftig.</summary>
    public UseSkillFailedReason Reason { get; }

    /// <summary>Nur bei Success aussagekräftig.</summary>
    public SkillType SkillType { get; }

    /// <summary>Nur bei SkillType SingleStrike/AreaAttack befüllt (leer bei Buff-Skills).</summary>
    public IReadOnlyList<(int TargetEntityId, AttackResult Result)> Hits { get; }

    /// <summary>Nur bei SkillType AttackBuff/AttackSpeedBuff gesetzt (null bei Schadens-Skills).</summary>
    public BuffType? BuffApplied { get; }

    private SkillUseResult(bool success, UseSkillFailedReason reason, SkillType skillType,
        IReadOnlyList<(int TargetEntityId, AttackResult Result)> hits, BuffType? buffApplied)
    {
        Success = success;
        Reason = reason;
        SkillType = skillType;
        Hits = hits;
        BuffApplied = buffApplied;
    }

    public static SkillUseResult Fail(UseSkillFailedReason reason) =>
        new(false, reason, default, Array.Empty<(int, AttackResult)>(), null);

    public static SkillUseResult HitSuccess(SkillType skillType, IReadOnlyList<(int TargetEntityId, AttackResult Result)> hits) =>
        new(true, default, skillType, hits, null);

    public static SkillUseResult BuffSuccess(SkillType skillType, BuffType buffType) =>
        new(true, default, skillType, Array.Empty<(int, AttackResult)>(), buffType);
}
