namespace Content.Server._Exodus.Mining.Pipes.Components;

/// <summary>
/// Joins the mining pipe network of this device with the network of a partner device without merging the
/// node groups, e.g. two ships whose bulk mining lasers are linked. Suppliers of joined networks are shared.
/// </summary>
[RegisterComponent]
public sealed partial class MiningPipeBridgeComponent : Component
{
    [ViewVariables]
    public EntityUid? Partner;
}
