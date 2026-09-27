using Content.Shared.Atmos;
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
}
