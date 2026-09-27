using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Weapons.Reflect;

/// <summary>Refills LimitedCharges when this entity successfully reflects an external shot.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ReflectChargeComponent : Component
{
    /// <summary>Charges gained from a shot; the LimitedCharges capacity still applies.</summary>
    [DataField]
    public int ChargesPerReflection = 1;
}
