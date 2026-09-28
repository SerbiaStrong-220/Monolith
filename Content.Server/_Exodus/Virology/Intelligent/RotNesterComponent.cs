using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Content.Shared.DoAfter;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Intelligent;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotNesterComponent : Component
{
    [DataField] public float SearchRange = 16;
    [DataField] public float HomeRange = 5;
    [DataField] public float PursuitLeash = 24;
    [DataField] public TimeSpan ThinkInterval = TimeSpan.FromSeconds(0.5);
    [DataField] public TimeSpan HomeSearchInterval = TimeSpan.FromSeconds(10);
    [DataField] public TimeSpan HomeRetryDelay = TimeSpan.FromSeconds(30);
    [DataField] public TimeSpan ProvisionTimeout = TimeSpan.FromSeconds(15);
    [DataField] public float ProvisionRange = 1.2f;
    [DataField] public float ExistingFoodRange = 2.5f;
    [DataField] public int LarvaePerPortion = 2;
    [DataField] public string VomitState = "vomit";
    [DataField] public EntityWhitelist Targets = new() { Components = ["HumanoidAppearance", "Actor"] };
    [DataField] public Robust.Shared.Map.EntityCoordinates Origin;
    [DataField] public TimeSpan FillDuration = TimeSpan.FromSeconds(120);
    [DataField] public TimeSpan Reserve;
    [DataField] public TimeSpan VomitDuration = TimeSpan.FromSeconds(2);
    [DataField] public TimeSpan ThreatMemory = TimeSpan.FromSeconds(20);
    [DataField] public TimeSpan ReactionHysteresis = TimeSpan.FromSeconds(3);
    [DataField] public EntityWhitelist Weapons = new() { Components = ["MeleeWeapon", "Gun", "Explosive"] };
    [DataField] public EntProtoId Food = "RotNutritionBlob";
    [ViewVariables, AutoPausedField] public Dictionary<EntityUid, TimeSpan> Aggressors = [];
    [DataField] public EntityUid? Threat;
    [DataField] public EntityUid? Larva;
    [DataField] public EntityUid? Home;
    [ViewVariables, AutoPausedField] public Dictionary<EntityUid, TimeSpan> RejectedHomes = [];
    [DataField] public DoAfterId? Vomit;
    [DataField] public bool Retaliating;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextThink;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextReserve;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan LastReserve;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan FleeUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan LarvaDeadline;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextHomeSearch;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotLarvaProvisionComponent : Component
{
    [DataField] public EntityUid Nester;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan Expires;
}
