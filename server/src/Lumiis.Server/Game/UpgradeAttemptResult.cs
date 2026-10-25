using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Ergebnis von World.TryUpgrade (M6c). FailureReason ist nur aussagekräftig, wenn die Aufwertung
/// gar nicht erst versucht wurde (dann ist weder Material noch Ausrüstung verändert). Ein tatsächlich
/// durchgeführter Versuch ist immer Success=true ODER Destroyed=true ODER ein Fehlschlag mit Schutzrolle
/// (beides false, Stufe unverändert) - in allen drei Fällen wurde der Schleifstein verbraucht.</summary>
public readonly record struct UpgradeAttemptResult(bool Success, bool Destroyed, int NewUpgradeLevel,
    UpgradeActionFailedReason FailureReason)
{
    /// <summary>True, wenn die Anfrage an einer Vorprüfung gescheitert ist und NICHTS verbraucht wurde.</summary>
    public bool Rejected => FailureReason != default;

    public static UpgradeAttemptResult Reject(UpgradeActionFailedReason reason)
    {
        return new UpgradeAttemptResult(false, false, 0, reason);
    }

    public static UpgradeAttemptResult Succeeded(int newUpgradeLevel)
    {
        return new UpgradeAttemptResult(true, false, newUpgradeLevel, default);
    }

    public static UpgradeAttemptResult FailedWithProtection(int currentUpgradeLevel)
    {
        return new UpgradeAttemptResult(false, false, currentUpgradeLevel, default);
    }

    public static UpgradeAttemptResult DestroyedItem()
    {
        return new UpgradeAttemptResult(false, true, 0, default);
    }
}
