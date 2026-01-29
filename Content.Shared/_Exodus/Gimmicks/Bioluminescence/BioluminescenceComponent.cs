using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Exodus.Gimmicks.Bioluminescence;

[RegisterComponent, NetworkedComponent]
public sealed partial class BioluminescenceComponent : Component
{
    [DataField]
    public EntProtoId ToggleActionPrototype = "ActionToggleBioluminescence";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public Color Color = Color.Yellow;
}
