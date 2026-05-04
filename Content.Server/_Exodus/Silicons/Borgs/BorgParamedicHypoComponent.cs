using Content.Shared.Chemistry.Reagent;
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
}
