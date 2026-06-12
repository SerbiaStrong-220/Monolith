using Robust.Shared.Containers;

namespace Content.Shared._Exodus.LifeInsurance.Components;

/// <summary>
/// Patient scanning capsule. A living player is placed inside and their DNA is recorded
/// onto the linked life insurance console.
/// </summary>
[RegisterComponent]
public sealed partial class LifeInsuranceScannerComponent : Component
{
    /// <summary>
    /// Container holding the body being scanned.
    /// </summary>
    [ViewVariables]
    public ContainerSlot BodyContainer = default!;

    /// <summary>
    /// Console this scanner is linked to.
    /// </summary>
    [ViewVariables]
    public EntityUid? ConnectedConsole;
}
