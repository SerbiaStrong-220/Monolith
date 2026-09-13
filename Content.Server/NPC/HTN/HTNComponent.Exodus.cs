// Exodus: allow simulation NPCs to keep working without an observing player.
namespace Content.Server.NPC.HTN;

public sealed partial class HTNComponent
{
    [DataField]
    public bool SleepWithoutPlayers = true;
}
