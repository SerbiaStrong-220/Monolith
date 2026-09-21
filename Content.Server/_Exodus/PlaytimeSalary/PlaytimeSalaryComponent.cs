using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.PlaytimeSalary;

/// <summary>Opts a controlled body or AI core into an account-wide playtime salary.</summary>
[RegisterComponent]
public sealed partial class PlaytimeSalaryComponent : Component
{
    /// <summary>Rate and payment interval shared by all eligible bodies using this salary.</summary>
    [DataField(required: true)]
    public ProtoId<PlaytimeSalaryPrototype> Salary;
}
