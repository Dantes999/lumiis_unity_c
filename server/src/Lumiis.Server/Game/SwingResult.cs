using System.Collections.Generic;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Ergebnis von World.TryPlayerSwing: bei Erfolg je getroffenem Gegner ein AttackResult (dieselben
/// Daten wie ein Einzeltreffer), bei Fehlschlag der Grund für das AttackFailedPacket.</summary>
public readonly struct SwingResult
{
    public bool Success { get; }
    public AttackFailedReason Reason { get; }
    public IReadOnlyList<(int TargetEntityId, AttackResult Result)> Hits { get; }

    private SwingResult(bool success, AttackFailedReason reason, IReadOnlyList<(int TargetEntityId, AttackResult Result)> hits)
    {
        Success = success;
        Reason = reason;
        Hits = hits;
    }

    public static SwingResult Fail(AttackFailedReason reason) =>
        new(false, reason, System.Array.Empty<(int, AttackResult)>());

    public static SwingResult Ok(IReadOnlyList<(int TargetEntityId, AttackResult Result)> hits) => new(true, default, hits);
}
