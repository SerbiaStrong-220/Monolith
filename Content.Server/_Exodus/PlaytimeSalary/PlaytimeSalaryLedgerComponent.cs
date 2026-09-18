using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.PlaytimeSalary;

/// <summary>Round-local playtime, independent of bodies, minds and reconnects.</summary>
[RegisterComponent]
public sealed partial class PlaytimeSalaryLedgerComponent : Component
{
    /// <summary>Next scheduled eligibility check.</summary>
    [ViewVariables]
    public TimeSpan NextCheck;

    /// <summary>Previous eligibility check, used to avoid crediting gaps in activity.</summary>
    [ViewVariables]
    public TimeSpan LastCheck;

    /// <summary>One account per player and salary; changing bodies never creates another payment stream.</summary>
    public readonly Dictionary<(NetUserId User, ProtoId<PlaytimeSalaryPrototype> Salary), PlaytimeSalaryAccount> Accounts = new();
}

public sealed class PlaytimeSalaryAccount
{
    /// <summary>Most recent check during which the player was eligible.</summary>
    public TimeSpan LastEligible;

    /// <summary>Eligible playtime not yet paid; retained while disconnected or in another role.</summary>
    public TimeSpan UnpaidTime;

    /// <summary>Fractional credit numerator in credit-ticks, carried between payments.</summary>
    public decimal Remainder;

    /// <summary>Prevents overlapping database payments for this account.</summary>
    public bool PaymentPending;

    /// <summary>Backoff after a failed database payment.</summary>
    public TimeSpan NextPaymentAttempt;
}
