using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Construction.Components;
using Robust.Shared.Map;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._Mono.NPC.HTN.Operators;

/// <summary>
/// Makes guns of parent shuttle fire at specified target key. Hands the targeting off to ShipTargetingSystem.
/// </summary>
public sealed partial class ShipFireGunsOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private IEntityManager _entManager = default!;
    private PowerReceiverSystem _power = default!;
    private ShipTargetingSystem _targeting = default!;

    /// <summary>
    /// When to shut the task down.
    /// </summary>
    [DataField]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    /// <summary>
    /// When this operator finishes, should we remove the target key?
    /// </summary>
    [DataField]
    public bool RemoveKeyOnFinish = true;

    /// <summary>
    /// Target EntityCoordinates to shoot at.
    /// </summary>
    [DataField]
    public string TargetKey = "ShipTargetCoordinates";

    /// <summary>
    /// How good to lead the target.
    /// </summary>
    [DataField]
    public float LeadingAccuracy = 1f;

    /// <summary>
    /// Whether to require us to be anchored.
    /// Here because HTN does not allow us to continuously check a condition by itself.
    /// Ignored if we're not anchorable.
    /// </summary>
    [DataField]
    public bool RequireAnchored = true;

    /// <summary>
    /// Whether to require us to be powered, if we have ApcPowerReceiver.
    /// </summary>
    [DataField]
    public bool RequirePowered = true;

    private const string TargetingCancelToken = "ShipTargetingCancelToken";
    private const string WasTargetKey = "ShipFireGunsWasTarget"; // Exodus shared HTN operator state fix

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _power = sysManager.GetEntitySystem<PowerReceiverSystem>();
        _targeting = sysManager.GetEntitySystem<ShipTargetingSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var targetCoordinates, _entManager))
        {
            return (false, null);
        }

        return (true, new Dictionary<string, object>()
        {
            {NPCBlackboard.OwnerCoordinates, targetCoordinates}
        });
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        // Need to remove the planning value for execution.
        blackboard.Remove<EntityCoordinates>(NPCBlackboard.OwnerCoordinates);
        var targetCoordinates = blackboard.GetValue<EntityCoordinates>(TargetKey);
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var comp = _targeting.Target(uid, targetCoordinates);

        if (comp == null)
            return;

        comp.LeadingAccuracy = LeadingAccuracy;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var target, _entManager)
            || !_entManager.TryGetComponent<TransformComponent>(owner, out var xform)
            // also fail if we're anchorable but are unanchored and require to be anchored
            || RequireAnchored
                && _entManager.TryGetComponent<AnchorableComponent>(owner, out var anchorable) && !xform.Anchored
            || RequirePowered
                && _entManager.TryGetComponent<ApcPowerReceiverComponent>(owner, out var receiver) && !_power.IsPowered(owner, receiver)
            // Exodus-begin powered NPC core targeting
            || !IsPoweredTarget(target)
            // Exodus-end
        )
            return HTNOperatorStatus.Failed;

        // hack to update ShipMoveTo or such when we swap targets
        // Exodus-begin shared HTN operator state fix
        if (blackboard.TryGetValue<EntityCoordinates>(WasTargetKey, out var wasTarget, _entManager) && wasTarget != target)
        {
            blackboard.Remove<EntityCoordinates>(WasTargetKey);
            return HTNOperatorStatus.Finished;
        }
        // Exodus-end

        // ensure we're still targeting if we e.g. move grids
        var comp = _targeting.Target(owner, target);

        blackboard.SetValue(WasTargetKey, target); // Exodus shared HTN operator state fix

        if (comp == null)
            return HTNOperatorStatus.Finished;

        if (target.EntityId == EntityUid.Invalid)
            return HTNOperatorStatus.Finished;

        // Exodus-begin faction NPC unavailable target fallback
        if (comp.TargetUnavailable)
        {
            _targeting.MarkTargetUnavailable(owner, target.EntityId);
            blackboard.Remove<EntityCoordinates>(TargetKey);
            return HTNOperatorStatus.Failed;
        }
        // Exodus-end

        if (ShutdownState == HTNPlanState.PlanFinished)
            return HTNOperatorStatus.Finished;

        return HTNOperatorStatus.Continuing;
    }

    // Exodus-begin powered NPC core targeting
    private bool IsPoweredTarget(EntityCoordinates target)
    {
        var targetUid = target.EntityId;
        if (targetUid == EntityUid.Invalid ||
            !_entManager.TryGetComponent<ShipNpcTargetComponent>(targetUid, out var targetComp) ||
            !targetComp.NeedPower)
        {
            return true;
        }

        return !_entManager.TryGetComponent<ApcPowerReceiverComponent>(targetUid, out var receiver) ||
               _power.IsPowered(targetUid, receiver);
    }
    // Exodus-end

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        // Cleanup the blackboard and remove steering.
        if (blackboard.TryGetValue<CancellationTokenSource>(TargetingCancelToken, out var cancelToken, _entManager))
        {
            cancelToken.Cancel();
            blackboard.Remove<CancellationTokenSource>(TargetingCancelToken);
        }

        if (RemoveKeyOnFinish)
            blackboard.Remove<EntityCoordinates>(TargetKey);

        blackboard.Remove<EntityCoordinates>(WasTargetKey); // Exodus shared HTN operator state fix

        _targeting.Stop(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        base.PlanShutdown(blackboard);

        ConditionalShutdown(blackboard);
    }
}
