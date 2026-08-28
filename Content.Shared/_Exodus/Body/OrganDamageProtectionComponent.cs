using System.Collections.Generic;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Body;

/// <summary>
/// Damage modifiers applied to a body while this organ is installed.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OrganDamageProtectionComponent : Component
{
    [DataField]
    public Dictionary<string, DamageModifierSetPrototype> Modifiers = new();
}
