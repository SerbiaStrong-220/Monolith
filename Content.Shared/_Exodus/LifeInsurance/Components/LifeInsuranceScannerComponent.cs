using System;
using Content.Shared.DoAfter;
using Robust.Shared.Containers;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.LifeInsurance.Components;

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

    /// <summary>
    /// How long climbing into the capsule takes.
    /// </summary>
    [DataField]
    public TimeSpan EnterDelay = TimeSpan.FromSeconds(2.5);
}

[Serializable, NetSerializable]
public sealed partial class LifeInsuranceScannerEnterDoAfterEvent : SimpleDoAfterEvent
{
}

[Serializable, NetSerializable]
public enum LifeInsuranceScannerVisuals : byte
{
    State
}

[Serializable, NetSerializable]
public enum LifeInsuranceScannerState : byte
{
    Open,
    Occupied
}
