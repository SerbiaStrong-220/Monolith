namespace Content.Server._Exodus.NPC.Command;

[RegisterComponent, Access(typeof(NpcCommandSystem))]
public sealed partial class NpcMinionComponent : Component
{
    /// <summary>
    /// If true, takes the reserved medic slot, if nothing it is a grunt that occupies a grunt slot.
    /// </summary>
    [DataField]
    public bool Medic;

    /// <summary>The commander currently commanding this NPC.</summary>
    [ViewVariables]
    public EntityUid? Commander;
}
