using Content.Shared.Humanoid;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Genetics;

/// <summary>Resources owned solely by the genetic abilities on this body.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class GeneticAbilityStateComponent : Component
{
    [DataField] public float TelekinesisRange = 10;
    [DataField] public TimeSpan TelekinesisPlacementDuration = TimeSpan.FromSeconds(0.35);
    [DataField] public SoundSpecifier TelekinesisSound = new SoundPathSpecifier("/Audio/Magic/forcewall.ogg")
    {
        Params = AudioParams.Default.WithVolume(-8f),
    };
    /// <summary>Runtime scope of the current telekinetic call; never grants range to ordinary clicks.</summary>
    public EntityUid? TelekinesisTarget;
    public EntityUid? TelekinesisTool;
    [DataField] public TimeSpan CloakCooldown = TimeSpan.FromSeconds(20);
    [DataField] public TimeSpan PryTime = TimeSpan.FromSeconds(5);
    [DataField] public SoundSpecifier PrySound = new SoundPathSpecifier("/Audio/Items/crowbar.ogg");
    /// <summary>The genome's last applied size factor, used to undo only its own contribution.</summary>
    [DataField] public float AppliedSizeMultiplier = 1f;
    [DataField] public TimeSpan ViewCheckInterval = TimeSpan.FromSeconds(0.5);
    [DataField] public EntProtoId ObservationEye = "GeneticObservationEye";
    [DataField] public bool Cloaked;
    [DataField, AutoPausedField] public TimeSpan CloakAvailable;
    [DataField, AutoPausedField] public TimeSpan NextViewCheck;
    // Serialized as detached data, never attached or resolved as an ECS component.
    [DataField] public HumanoidAppearanceComponent? OriginalAppearance;
    [DataField] public string? OriginalName;
    public EntityUid? ViewTarget;
    public EntityUid? ViewEye;
    public ICommonSession? ViewSession;
}
