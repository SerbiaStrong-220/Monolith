using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.PlaytimeSalary;

/// <summary>OOC savings rewards for time spent controlling explicitly eligible entities.</summary>
[Prototype]
public sealed partial class PlaytimeSalaryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Credits earned per hour of eligible playtime.</summary>
    [DataField(required: true)]
    public int HourlyRate;

    /// <summary>Eligible playtime required between payments, excluding absences and AFK time.</summary>
    [DataField(required: true)]
    public TimeSpan PayoutInterval;

    /// <summary>Private system message sent after a successful deposit.</summary>
    [DataField(required: true)]
    public LocId PaymentMessage;
}
