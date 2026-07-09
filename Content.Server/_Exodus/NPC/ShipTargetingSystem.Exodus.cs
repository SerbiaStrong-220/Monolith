using Content.Server._Exodus.NPC;
using Content.Shared._Exodus.NPC.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Mono.NPC.HTN;

public sealed partial class ShipTargetingSystem
{
    [Dependency] private IGameTiming _exodusTiming = default!;
    [Dependency] private NpcFactionSystem _exodusNpcFaction = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _exodusFactionQuery;
    [Dependency] private EntityQuery<FactionAiControlledGridComponent> _exodusFactionAiGridQuery;
    [Dependency] private EntityQuery<FactionNpcAiCoreComponent> _exodusFactionAiCoreQuery;

    private static readonly TimeSpan UnavailableTargetCooldown = TimeSpan.FromSeconds(3);

    public void MarkTargetUnavailable(EntityUid owner, EntityUid target)
    {
        if (TerminatingOrDeleted(owner) || TerminatingOrDeleted(target))
            return;

        var unavailable = EnsureComp<ShipNpcUnavailableTargetsComponent>(owner);
        unavailable.Targets[target] = _exodusTiming.CurTime + UnavailableTargetCooldown;
    }

    private bool CanFireWithoutFactionFriendlyFire(
        EntityUid sourceUid,
        EntityUid sourceGrid,
        EntityUid targetUid,
        EntityUid weaponUid,
        MapCoordinates targetMapPos)
    {
        if (!TryGetFactionFireSource(sourceUid, sourceGrid, out var source, out var sourceFaction, out var sourceCore))
            return true;

        if (TerminatingOrDeleted(targetUid) || TerminatingOrDeleted(weaponUid))
            return true;

        var weaponMapPos = _transform.GetMapCoordinates(Transform(weaponUid));
        if (weaponMapPos.MapId != targetMapPos.MapId)
            return false;

        var targetVector = targetMapPos.Position - weaponMapPos.Position;
        var distance = targetVector.Length();
        if (distance <= float.Epsilon)
            return true;

        var targetGrid = Transform(targetUid).GridUid;
        var ray = new CollisionRay(weaponMapPos.Position, targetVector / distance, (int) CollisionGroup.BulletImpassable);
        var state = new FactionFriendlyFireRayState(this, source, sourceFaction, sourceCore, sourceGrid, targetUid, targetGrid, weaponUid);

        foreach (var _ in _physics.IntersectRayWithPredicate(targetMapPos.MapId, ray, state, ShouldIgnoreFactionFriendlyFireHit, distance, returnOnFirstHit: true))
        {
            return false;
        }

        return true;
    }

    private bool TryGetFactionFireSource(
        EntityUid sourceUid,
        EntityUid sourceGrid,
        out EntityUid source,
        out NpcFactionMemberComponent? faction,
        out FactionNpcAiCoreComponent core)
    {
        if (!_exodusFactionAiCoreQuery.TryGetComponent(sourceUid, out var coreComp) || coreComp == null)
        {
            source = default;
            faction = null;
            core = default!;
            return false;
        }

        core = coreComp;

        if (_exodusFactionQuery.TryGetComponent(sourceUid, out faction))
        {
            source = sourceUid;
            return true;
        }

        if (_exodusFactionQuery.TryGetComponent(sourceGrid, out faction))
        {
            source = sourceGrid;
            return true;
        }

        source = sourceUid;
        faction = null;
        return true;
    }

    private static bool ShouldIgnoreFactionFriendlyFireHit(EntityUid hit, FactionFriendlyFireRayState state)
    {
        return !state.System.IsFactionFriendlyFireBlocker(hit, state);
    }

    private bool IsFactionFriendlyFireBlocker(EntityUid hit, FactionFriendlyFireRayState state)
    {
        if (hit == state.Weapon || hit == state.Source || hit == state.Target || TerminatingOrDeleted(hit))
            return false;

        if (!TryComp(hit, out TransformComponent? hitXform) || hitXform.GridUid is not { } hitGrid)
            return false;

        if (hitGrid == state.SourceGrid || hitGrid == state.TargetGrid || TerminatingOrDeleted(hitGrid))
            return false;

        return IsFriendlyNpcFireTarget(state.Source, state.SourceFaction, state.SourceCore, hit, hitGrid);
    }

    private bool IsFriendlyNpcFireTarget(
        EntityUid source,
        NpcFactionMemberComponent? sourceFaction,
        FactionNpcAiCoreComponent sourceCore,
        EntityUid target,
        EntityUid targetGrid)
    {
        if (_exodusFactionAiGridQuery.TryGetComponent(targetGrid, out var control) &&
            control.State == FactionAiControlState.Controlled &&
            control.Faction is { } controlFaction &&
            HasFriendlyFaction(source, sourceFaction, sourceCore, controlFaction))
        {
            return true;
        }

        if (_exodusFactionQuery.TryGetComponent(target, out var targetFaction))
            return HasFriendlyFaction(source, sourceFaction, sourceCore, target, targetFaction);

        return _exodusFactionQuery.TryGetComponent(targetGrid, out targetFaction) &&
               HasFriendlyFaction(source, sourceFaction, sourceCore, targetGrid, targetFaction);
    }

    private bool HasFriendlyFaction(
        EntityUid source,
        NpcFactionMemberComponent? sourceFaction,
        FactionNpcAiCoreComponent sourceCore,
        EntityUid target,
        NpcFactionMemberComponent targetFaction)
    {
        return sourceCore.IgnoredFactions.Overlaps(targetFaction.Factions) ||
               sourceFaction != null && _exodusNpcFaction.IsEntityFriendly((source, sourceFaction), (target, targetFaction));
    }

    private bool HasFriendlyFaction(
        EntityUid source,
        NpcFactionMemberComponent? sourceFaction,
        FactionNpcAiCoreComponent sourceCore,
        ProtoId<NpcFactionPrototype> targetFaction)
    {
        return sourceCore.IgnoredFactions.Contains(targetFaction) ||
               sourceFaction != null && _exodusNpcFaction.IsFactionFriendlyOrSame(targetFaction, (source, sourceFaction));
    }

    private readonly record struct FactionFriendlyFireRayState(
        ShipTargetingSystem System,
        EntityUid Source,
        NpcFactionMemberComponent? SourceFaction,
        FactionNpcAiCoreComponent SourceCore,
        EntityUid SourceGrid,
        EntityUid Target,
        EntityUid? TargetGrid,
        EntityUid Weapon);
}
