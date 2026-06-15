using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Store.Components;

[RegisterComponent]
public sealed partial class TerritoryStoreDiscountComponent : Component
{
    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> Faction = default!;

    [DataField]
    public float DiscountPerPoint = 0.04f;
}
