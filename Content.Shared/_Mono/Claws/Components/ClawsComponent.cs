using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.Claws.Components;

/// <summary>
/// This is claw component used for <see cref="SharedClawsSystem"/> System.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)] // Exodus: apply claw effects after client state arrives.
public sealed partial class ClawsComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<ClawPrototype> ClawStage;

    [DataField, AutoNetworkedField]
    public Dictionary<int, ProtoId<ClawPrototype>> Claws = new(); // Exodus: safe until the first network state arrives.

    [DataField]
    public LocId? ClawGrowthNotification;

    [DataField]
    public TimeSpan GrowTimer = TimeSpan.Zero;

    [DataField]
    public TimeSpan AccumulatedBonusGrowth = TimeSpan.Zero;

    [DataField]
    public TimeSpan DeclawItemHoldTimer = TimeSpan.Zero;

    // Exodus-begin: restore only settings owned by this claw component on removal.
    public bool OriginalWideSwing;
    public bool OriginalAltDisarm;
    public float? OriginalSpread;
    public Content.Shared._DV.Weapons.Ranged.Components.PlayerAccuracyModifierComponent? AppliedAccuracy;
    public bool CapturedMelee;
    // Exodus-end
}
