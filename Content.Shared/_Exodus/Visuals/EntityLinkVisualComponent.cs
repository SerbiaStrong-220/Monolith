using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Visuals;

/// <summary>
/// Draws a persistent visual link from this entity to <see cref="Target"/>.
/// The link remains active until the server clears the target or either endpoint leaves the client's view.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EntityLinkVisualComponent : Component
{
    /// <summary>
    /// Current target. Null disables the link without removing the component.
    /// </summary>
    [AutoNetworkedField]
    public EntityUid? Target;

    /// <summary>
    /// Visual style used to draw the link.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<EntityLinkVisualPrototype> Style = default!;

    /// <summary>
    /// Local-space offset from the source entity's origin.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 SourceOffset;

    /// <summary>
    /// Local-space offset from the target entity's origin.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 TargetOffset;
}
