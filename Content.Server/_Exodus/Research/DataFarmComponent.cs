// (c) Space Exodus Team - EXDS-RL
// Authors: DarkBanOne

using Content.Shared.Atmos;
using Content.Shared._Exodus.Research.Visuals;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Research.Components;

[RegisterComponent, AutoGenerateComponentState]
public sealed partial class DataFarmComponent : Component
{
    [DataField]
    public GasMixture Buffer = new();

    [DataField, AutoNetworkedField]
    public DataFarmState CurrentState = DataFarmState.Off;

    [DataField]
    public TimeSpan DestroyTimer = TimeSpan.FromSeconds(120f);

    [DataField]
    public TimeSpan CycleDuration = TimeSpan.FromSeconds(1f);

    [DataField]
    public TimeSpan? NextAt;

    [DataField]
    public TimeSpan? NextDamageAt;

    [DataField]
    public bool Powered = false;

    [DataField]
    public ProtoId<DamageTypePrototype> DamageType = "Heat";

    [DataField]
    public float DeltaT = 35f;

    [DataField]
    public float MinTemp = 268.15f;

    [DataField]
    public float MaxTemp = 323.15f;

    [DataField]
    public float MinMolesOnTile = 5f;

    [DataField]
    public float MinPressure = 20f;

    [DataField]
    public float IntakePerSecond = 10f;

    [DataField]
    public string InletName = "inlet";

    [DataField]
    public bool Enabled = true;

    [DataField]
    public TimeSpan StartupDuration = TimeSpan.FromSeconds(3f);

    [DataField, AutoNetworkedField]
    public SoundSpecifier? NormalSound = new SoundPathSpecifier("/Audio/_Exodus/Machines/DataMinerResearch/normal.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? ProcessSound = new SoundPathSpecifier("/Audio/_Exodus/Machines/DataMinerResearch/onn.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? WarningSound = new SoundPathSpecifier("/Audio/_Exodus/Machines/DataMinerResearch/nogood.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? ErrorSound = new SoundPathSpecifier("/Audio/_Exodus/Machines/DataMinerResearch/error.ogg");
}
