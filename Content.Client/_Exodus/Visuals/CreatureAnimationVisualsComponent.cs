namespace Content.Client._Exodus.Visuals;

/// <summary>Sprite cycles driven by replicated movement, melee cooldown and mob state.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CreatureAnimationVisualsComponent : Component
{
    [DataField]
    public int Layer;

    [DataField(required: true)]
    public string IdleState = default!;

    [DataField]
    public string? MovingState;

    [DataField]
    public string? AttackState;

    [DataField]
    public TimeSpan AttackDuration;

    [DataField]
    public string? SpawnState;

    [DataField]
    public TimeSpan SpawnDuration;

    [DataField]
    public string? DyingState;

    [DataField]
    public TimeSpan DeathDuration;

    [DataField]
    public string? DeadState;

    [ViewVariables]
    public bool AnimationStarted;

    [ViewVariables]
    public bool WasDead;

    [ViewVariables]
    public string? CurrentState;

    [ViewVariables]
    public int? OriginalDrawDepth;

    [DataField, AutoPausedField]
    public TimeSpan SpawnUntil;

    [DataField, AutoPausedField]
    public TimeSpan AttackUntil;

    [DataField, AutoPausedField]
    public TimeSpan DeathUntil;

    [DataField, AutoPausedField]
    public TimeSpan LastAttack;
}
