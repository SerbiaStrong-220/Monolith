using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Silicons.Borgs;

[RegisterComponent]
public sealed partial class BorgParamedicHypoComponent : Component
{
    [DataField]
    public string SolutionName = "hypospray";

    [DataField]
    public List<ProtoId<ReagentPrototype>> Reagents = new()
    {
        "Bicaridine",
        "Tricordrazine",
        "Dylovene",
        "Epinephrine",
    };

    [DataField]
    public int CurrentReagent;

    [DataField]
    public FixedPoint2 RechargeAmount = FixedPoint2.New(5);

    [DataField]
    public TimeSpan RechargeInterval = TimeSpan.FromSeconds(30);

    [DataField]
    public TimeSpan NextRecharge = TimeSpan.Zero;
}
