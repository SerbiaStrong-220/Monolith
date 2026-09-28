using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Body;

[RegisterComponent, NetworkedComponent]
public sealed partial class RestingRegenerationComponent : Component
{
    [DataField]
    public float RestingMultiplier = 2f;

    /// <summary>Damage that regeneration cannot heal without functioning pressure-breathing lungs.</summary>
    [DataField]
    public HashSet<ProtoId<DamageTypePrototype>> RequiresBreathing = new();
}
