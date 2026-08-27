using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.Abilities;

[RegisterComponent, Access(typeof(NpcSuicideBombSystem))]
public sealed partial class NpcSuicideBombComponent : Component
{
    /// <summary>Action granted for this ability (HTN uses this id).</summary>
    [DataField]
    public EntProtoId Action = "ActionDetonateNPC";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>Distance (tiles) to the target at which mob do action. Goes to HTN as BombRange.</summary>
    [DataField]
    public float DetonateRange = 1.5f;

    /// <summary>AoE for apply effects.</summary>
    [DataField]
    public float Radius = 2f;

    /// <summary>Damage dealt to every mob (not structures) caught in AoE.</summary>
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>Bleeding added to every mob caught in AoE.</summary>
    [DataField]
    public float Bleed;

    /// <summary>Reagents spilled as a puddle at the blast centre.</summary>
    [DataField]
    public Solution? Spill;

    /// <summary>Visual effect entity spawned at the blast centre.</summary>
    [DataField]
    public EntProtoId? Effect;

    /// <summary>Sound played on activation.</summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/explosion1.ogg");

    /// <summary>True this if u want a real explosion (damages structures), tuned below.</summary>
    [DataField]
    public bool Explode;

    /// <summary>Explosion damage type.</summary>
    [DataField]
    public ProtoId<ExplosionPrototype> ExplosionType = "Default";

    /// <summary>Total explosion intensity.</summary>
    [DataField]
    public float ExplosionTotalIntensity = 30f;

    /// <summary>Intensity falloff per tile from the centre.</summary>
    [DataField]
    public float ExplosionSlope = 5f;

    /// <summary>Intensity cap on any single tile.</summary>
    [DataField]
    public float ExplosionMaxTileIntensity = 10f;

    /// <summary>Use this if u want mob to trigger action immediately if hp range from 0 to 1(100%).</summary>
    [DataField]
    public float? HealthTrigger;

    /// <summary>Runtime guard so it only ever detonates once.</summary>
    [ViewVariables]
    public bool Detonated;
}
