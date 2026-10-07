using Content.Shared.Atmos;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Materials;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Exodus.Mining.Pipes;

/// <summary>Exhaust buffer, corrosion and overpressure settings for a lathe supplied with liquid metal.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class MiningRefineryComponent : Component
{
    [DataField]
    public string ExhaustNode = "exhaust";

    [DataField, AutoNetworkedField]
    public Gas ExhaustGas = Gas.ChlorineTrifluoride;

    [DataField, AutoNetworkedField]
    public ProtoId<MaterialPrototype> SlurryMaterial = "MiningSlurry";

    /// <summary>Readings for the open interface; the gas mixture itself stays on the server.</summary>
    [ViewVariables, AutoNetworkedField]
    public MiningRefineryStorageState StorageState;

    /// <summary>Part whose upgrades increase the liquid metal and exhaust capacities.</summary>
    [DataField]
    public ProtoId<MachinePartPrototype> MachinePartCapacity = "MatterBin";

    /// <summary>Capacity multiplier for each part rating. Unlisted ratings use their numeric rating as the multiplier.</summary>
    [DataField]
    public Dictionary<int, float> CapacityMultipliers = new();

    /// <summary>Current multiplier; saved machines may already be map-initialized when loaded.</summary>
    [DataField]
    public float CapacityMultiplier = 1f;

    /// <summary>Part whose upgrades increase the exhaust produced per batch.</summary>
    [DataField]
    public ProtoId<MachinePartPrototype> MachinePartExhaust = "Manipulator";

    /// <summary>Exhaust yield multiplier for each part rating. Unlisted ratings give no bonus.</summary>
    [DataField]
    public Dictionary<int, float> ExhaustMultipliers = new();

    /// <summary>Current exhaust yield multiplier, preserved when loading an upgraded machine.</summary>
    [DataField]
    public float ExhaustMultiplier = 1f;

    /// <summary>Maximum material discount at a full local liquid metal tank; zero disables it.</summary>
    [DataField]
    public float MaxFullnessDiscount;

    /// <summary>Item slot IDs accepting consumable siphon filters. Also used as appearance layer keys.</summary>
    [DataField]
    public List<string> FilterSlots = new();

    /// <summary>Sprite states for installed working and depleted cartridges.</summary>
    [DataField]
    public string FilterIntactState = "mounted";

    [DataField]
    public string FilterDepletedState = "mounted-depleted";

    /// <summary>Material discount per working filter, additive within this set of filters.</summary>
    [DataField]
    public float DiscountPerFilter = 0.025f;

    /// <summary>Fullness and filter multiplier already applied to the lathe; serialized to prevent compounding.</summary>
    [DataField]
    public float EfficiencyMultiplier = 1f;

    /// <summary>Current discount from the local tank, excluding connected suppliers.</summary>
    [ViewVariables]
    public float FullnessDiscount;

    /// <summary>Current number of installed filters, including depleted cartridges.</summary>
    [ViewVariables]
    public int InstalledFilters;

    /// <summary>Current number of installed filters that still have remaining life.</summary>
    [ViewVariables]
    public int ActiveFilters;

    /// <summary>Original liquid metal limit, captured before upgrades and saved to prevent compounding on load.</summary>
    [DataField]
    public int? BaseSlurryCapacity;

    /// <summary>Original exhaust volume, captured before upgrades and preserved when saving the machine.</summary>
    [DataField]
    public float? BaseExhaustVolume;

    /// <summary>Original corrosion threshold, captured before upgrades and preserved when saving the machine.</summary>
    [DataField]
    public float? BaseCorrosionThreshold;

    /// <summary>Original explosion threshold, captured before upgrades and preserved when saving the machine.</summary>
    [DataField]
    public float? BaseExplosionThreshold;

    [DataField]
    public float ExhaustMolesPerBatch = 10f;

    [DataField]
    public GasMixture Exhaust = new(200) { Temperature = Atmospherics.T20C };

    [DataField]
    public float ExhaustMolesPerSecond = 5f;

    /// <summary>Backed-up exhaust above this amount starts the corrosion timer.</summary>
    [DataField, AutoNetworkedField]
    public float CorrosionThreshold = 200f;

    [DataField]
    public TimeSpan CorrosionDelay = TimeSpan.FromSeconds(30);

    [DataField]
    public DamageSpecifier CorrosionDamage = new();

    [DataField]
    public TimeSpan CorrosionTime;

    /// <summary>Detonate the machine's Explosive component at this many buffered moles. Zero disables this.</summary>
    [DataField, AutoNetworkedField]
    public float ExplosionThreshold = 400f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate;

    /// <summary>
    /// Consortium bonus currently folded into the lathe material multiplier.
    /// Saved with the machine so that loading it without its links removes the bonus instead of compounding it.
    /// </summary>
    [DataField]
    public float LinkBonus;

    /// <summary>
    /// Compatibility with older saves that also applied <see cref="LinkBonus"/> to refining speed.
    /// Cleared after migrating a saved bonus, on map initialization, or when applying a new material-only bonus.
    /// </summary>
    [DataField]
    public bool LinkBonusAffectsSpeed = true;

    /// <summary>Ships whose liquid metal networks are currently joined with this refinery's network.</summary>
    [ViewVariables]
    public int LinkedShips = 1;
}
