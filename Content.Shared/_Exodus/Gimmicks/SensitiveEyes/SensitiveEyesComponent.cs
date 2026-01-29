using Robust.Shared.GameStates;

namespace Content.Shared.Exodus.Gimmicks.SensitiveEyes;

[RegisterComponent, NetworkedComponent]
public sealed partial class SensitiveEyesComponent : Component
{
    [DataField]
    public float DurationModifier = 1.2f;
}
