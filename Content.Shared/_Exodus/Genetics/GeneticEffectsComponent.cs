using Content.Shared.Inventory;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Genetics;

/// <summary>Only expressed effects are replicated; the genome and round cipher stay on the server.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class GeneticEffectsComponent : Component
{
    [DataField, AutoNetworkedField] public GeneticModifiers Modifiers = new();
    /// <summary>Alternate form of this same entity; never a replacement body.</summary>
    [DataField, AutoNetworkedField] public bool InAlternateForm;
    [DataField, AutoNetworkedField] public bool NightVisionEnabled;
    [DataField, AutoNetworkedField] public Color NightVisionColor = Color.FromHex("#344837");
    [DataField, AutoNetworkedField] public bool HearingEnabled;
    [DataField, AutoNetworkedField] public float HearingRange = 5f;
    /// <summary>Slot definitions contributed by the biological pocket mutation.</summary>
    [DataField, AutoNetworkedField] public ProtoId<InventoryTemplatePrototype> PocketTemplate = "GeneticPocket";
    /// <summary>Minimum interval between local feedback messages while a blocked weapon is held on auto-fire.</summary>
    [DataField] public TimeSpan BlockedShotPopupInterval = TimeSpan.FromSeconds(3);
    /// <summary>Client-only feedback timestamp; deliberately not rolled back with predicted component state.</summary>
    [ViewVariables] public TimeSpan NextBlockedShotPopup;
    public bool Reverting;
}
