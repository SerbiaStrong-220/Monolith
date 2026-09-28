using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Virology.Intelligent;

/// <summary>Ownership is explicit and inherited by offspring; infection alone never grants colony vision.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RotColonyMemberComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid? Core;
    [DataField, AutoNetworkedField] public bool Connected;
    [DataField] public bool NeedsSupport = true;
    [DataField] public bool Conductive;
    [DataField] public bool Tissue;
    [DataField] public Vector2i Size = Vector2i.One;
    [DataField] public float VisionRange;
    [DataField] public float Income;
    [DataField] public float Refund;
    [DataField] public bool RequiresExhaust;
}

[ByRefEvent]
public readonly record struct RotConnectionChangedEvent(bool Connected);
