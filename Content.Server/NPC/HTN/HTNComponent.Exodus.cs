// Exodus-begin: allow finite-lived simulation NPCs to work without an observing player.
namespace Content.Server.NPC.HTN;

public sealed partial class HTNComponent
{
    [DataField]
    public bool SleepWithoutPlayers = true;
}
// Exodus-end
