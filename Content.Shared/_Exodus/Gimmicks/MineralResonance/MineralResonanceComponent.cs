using Content.Shared.Actions;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Exodus.Gimmicks.MineralResonance;

[RegisterComponent, NetworkedComponent]
public sealed partial class MineralResonanceComponent : Component
{
    [DataField]
    public EntProtoId ActionPrototype = "ActionMineralResonance";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public ProtoId<EmotePrototype> TriggerEmote = "JawsSnap";

    [DataField]
    public float ViewRange = 5f;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(3.5f);
}

public sealed partial class MineralResonanceUseEvent : InstantActionEvent
{

}
