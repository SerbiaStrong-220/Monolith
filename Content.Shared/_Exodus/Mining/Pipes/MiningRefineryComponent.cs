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
    /// Consortium bonus currently folded into the lathe time and material multipliers.
    /// Saved with the machine so that loading it without its links removes the bonus instead of compounding it.
    /// </summary>
    [DataField]
    public float LinkBonus;

    /// <summary>Ships whose liquid metal networks are currently joined with this refinery's network.</summary>
    [ViewVariables]
    public int LinkedShips = 1;
}
