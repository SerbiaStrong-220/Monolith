using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Lifecycle;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotNestComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Larva;

    [DataField(required: true)]
    public EntityTableSelector Vines = default!;

    /// <summary>The planting roll belongs to this nest and is never repeated when its tile is blocked.</summary>
    [DataField]
    public List<EntProtoId>? SelectedVines;

    [DataField]
    public EntityWhitelist? ReplaceableVines;

    [DataField]
    public TimeSpan SpawnInterval = TimeSpan.FromMinutes(2);

    [DataField]
    public int Capacity = 2;

    [DataField]
    public HashSet<EntityUid> Larvae = [];

    [DataField]
    public bool Seeded;

    [DataField, AutoPausedField]
    public TimeSpan NextSpawn;
}
