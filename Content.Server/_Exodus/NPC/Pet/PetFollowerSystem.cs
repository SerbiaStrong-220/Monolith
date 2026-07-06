using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.NPC.Pet;
using Robust.Shared.Map;

namespace Content.Server._Exodus.NPC.Pet;

public sealed partial class PetFollowerSystem : EntitySystem
{
    [Dependency] private NPCSystem _npc = default!;

    private const string FollowRangeKey = "FollowRange";
    private const string FollowCloseRangeKey = "FollowCloseRange";
    private const string MinIdleTimeKey = "MinimumIdleTime";
    private const string MaxIdleTimeKey = "MaximumIdleTime";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PetFollowerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PetFollowerComponent, PetOwnerChangedEvent>(OnOwnerChanged);
    }

    private void OnMapInit(Entity<PetFollowerComponent> ent, ref MapInitEvent args)
    {
        ApplyRanges(ent);
    }

    private void OnOwnerChanged(Entity<PetFollowerComponent> ent, ref PetOwnerChangedEvent args)
    {
        ApplyRanges(ent);

        if (args.Master is { } master)
            _npc.SetBlackboard(ent, NPCBlackboard.FollowTarget, new EntityCoordinates(master, Vector2.Zero));
    }

    private void ApplyRanges(Entity<PetFollowerComponent> ent)
    {
        _npc.SetBlackboard(ent, FollowRangeKey, ent.Comp.FollowRange);
        _npc.SetBlackboard(ent, FollowCloseRangeKey, ent.Comp.FollowCloseRange);
        // The follow tree reads these blackboard keys as float seconds.
        _npc.SetBlackboard(ent, MinIdleTimeKey, (float) ent.Comp.IdleTimeMin.TotalSeconds);
        _npc.SetBlackboard(ent, MaxIdleTimeKey, (float) ent.Comp.IdleTimeMax.TotalSeconds);
    }
}
