using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared._Exodus.NPC.Abilities;
using Content.Shared.DoAfter;

namespace Content.Server._Exodus.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class ProgressBarOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private SharedDoAfterSystem _doAfter = default!;

    /// <summary>Blackboard key holding fake DoAfter duration in seconds.</summary>
    [DataField(required: true)]
    public string Key = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _doAfter = sysManager.GetEntitySystem<SharedDoAfterSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (blackboard.TryGetValue<float>(Key, out var seconds, _entManager) && seconds > 0f)
        {
            // eventTarget must be a real entity (so we dont catch DebugAssertException)
            var args = new DoAfterArgs(_entManager, owner, seconds, new NpcCastDoAfterEvent(), owner)
            {
                BreakOnMove = true,
                RequireCanInteract = false,
            };
            _doAfter.TryStartDoAfter(args);
        }

        return HTNOperatorStatus.Finished;
    }
}
