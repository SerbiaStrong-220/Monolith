using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Chemistry;

[RegisterComponent]
public sealed partial class ReagentAutoRechargeComponent : Component
{
    [DataField]
    public string SolutionName = "hypospray";

    [DataField]
    public ProtoId<ReagentPrototype> Reagent = "Bicaridine";

    [DataField]
    public FixedPoint2 RechargeAmount = FixedPoint2.New(5);

    [DataField]
    public TimeSpan RechargeInterval = TimeSpan.FromSeconds(30);

    [DataField]
    public TimeSpan NextRecharge = TimeSpan.Zero;
}
