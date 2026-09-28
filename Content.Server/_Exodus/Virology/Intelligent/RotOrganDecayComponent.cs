using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Intelligent;

[RegisterComponent]
public sealed partial class RotOrganDecayComponent : Component
{
    [DataField(required: true)] public EntProtoId Effect;
    public bool Played;
}
