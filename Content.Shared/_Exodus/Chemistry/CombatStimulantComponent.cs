using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Chemistry;

/// <summary>A temporary, source-owned movement and melee stimulant. Repeated doses extend its duration.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class CombatStimulantComponent : Component
{
    /// <summary>End of the short combat effect, independent of any chemical dependency reserve.</summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan ExpiresAt;

    /// <summary>Multipliers applied to the user's movement and melee attack rate.</summary>
    [DataField, AutoNetworkedField]
    public float MovementMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float AttackRateMultiplier = 1f;
}
