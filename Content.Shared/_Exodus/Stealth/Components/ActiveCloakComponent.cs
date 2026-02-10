using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Exodus.Stealth.Components;

/// <summary>
/// Active cloak that can be toggled on/off. Breaks when wearer is hit or attacks.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ActiveCloakSystem))]
public sealed partial class ActiveCloakComponent : Component
{
    /// <summary>
    /// Stealth configuration when cloak is active
    /// </summary>
    [DataField, AutoNetworkedField]
    public StealthData Stealth = new();

    /// <summary>
    /// Whether the cloak is currently enabled
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = false;

    /// <summary>
    /// Time when cloak was broken (for cooldown tracking)
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? BrokenTime = null;

    /// <summary>
    /// Cooldown duration after cloak breaks
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Action entity for toggling cloak
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ToggleAction;

    /// <summary>
    /// Action prototype to grant
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId? ToggleActionId = "ActionToggleActiveCloak";

    /// <summary>
    /// Sound played when cloak is activated
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? ActivateSound = new SoundPathSpecifier("/Audio/_White/Items/Goggles/activate.ogg");

    /// <summary>
    /// Sound played when cloak is deactivated
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? DeactivateSound = new SoundPathSpecifier("/Audio/_White/Items/Goggles/deactivate.ogg");

    /// <summary>
    /// Sound played when cloak breaks
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? BreakSound = null;
}
