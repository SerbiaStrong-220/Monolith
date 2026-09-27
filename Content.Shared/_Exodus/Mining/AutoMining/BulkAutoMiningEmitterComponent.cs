using Content.Shared.Damage;
using Content.Shared.Materials;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Mining.AutoMining;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
[Access(typeof(SharedBulkAutoMiningSystem))]
public sealed partial class BulkAutoMiningEmitterComponent : Component
{
    /// <summary>Optional localized short name for the console; the entity keeps its full name.</summary>
    [DataField]
    public LocId? ConsoleName;

    [DataField]
    public EntityWhitelist? ForbiddenTargets;

    [DataField]
    public DamageSpecifier ForbiddenTileDamage = new();

    [DataField]
    public ProtoId<MaterialPrototype> SlurryMaterial = "MiningSlurry";

    /// <summary>Material volume produced by clearing one intact tile.</summary>
    [DataField]
    public int SlurryPerTile = 200;

    /// <summary>Distance from the head pivot to the forward lens, in world units.</summary>
    [DataField]
    public float MuzzleOffset = 0.75f;

    [DataField]
    public SoundSpecifier? StartSound;

    [ViewVariables]
    public EntityUid? StartupStream;

    [ViewVariables]
    public EntityUid? Controller;

    /// <summary>Persists between jobs so restarting or changing consoles cannot accelerate excavation.</summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextMiningTime;

    /// <summary>Bounds failed target searches independently of the other lasers' work cycles.</summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextTargetSearchTime;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? BeamGrid;

    [ViewVariables, AutoNetworkedField]
    public Vector2i BeamTile;
}
