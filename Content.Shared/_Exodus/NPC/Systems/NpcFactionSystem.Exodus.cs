using Content.Shared.NPC.Components;

namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    public void RemoveFactionAndRemoveEmpty(Entity<NpcFactionMemberComponent?> ent, string faction)
    {
        RemoveFaction(ent, faction);

        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (ent.Comp.Factions.Count == 0)
            RemComp<NpcFactionMemberComponent>(ent.Owner);
    }
}
