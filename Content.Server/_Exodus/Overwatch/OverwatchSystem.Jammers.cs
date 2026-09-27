using Content.Server._Mono.Overwatch.Components;
using Content.Shared._Mono.Company;
using Content.Shared._Rat.Overwatch;
using Content.Shared.Sticky;
using Content.Shared.Sticky.Components;

namespace Content.Server._Rat.Overwatch;

public sealed partial class OverwatchSystem
{
    private EntityQuery<JamOverwatchOnStuckComponent> _jammerQuery;
    private EntityQuery<StickyComponent> _jammerStickyQuery;
    private EntityQuery<TransformComponent> _jammerTransformQuery;

    private void InitializeJammers()
    {
        _jammerQuery = GetEntityQuery<JamOverwatchOnStuckComponent>();
        _jammerStickyQuery = GetEntityQuery<StickyComponent>();
        _jammerTransformQuery = GetEntityQuery<TransformComponent>();
        SubscribeLocalEvent<JamOverwatchOnStuckComponent, EntityStuckEvent>(OnJammerStuck);
        SubscribeLocalEvent<JamOverwatchOnStuckComponent, EntityUnstuckEvent>(OnJammerUnstuck);
        SubscribeLocalEvent<JamOverwatchOnStuckComponent, ComponentShutdown>(OnJammerShutdown);
    }

    private bool IsOverwatchJammed(EntityUid target)
    {
        if (!_jammerTransformQuery.TryGetComponent(target, out var transform))
            return false;

        // Sticky items are direct children of their target inside its sticker container.
        var children = transform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (_jammerQuery.TryGetComponent(child, out var jammer) && jammer.LifeStage < ComponentLifeStage.Stopping &&
                _jammerStickyQuery.TryGetComponent(child, out var sticky) && sticky.StuckTo == target)
                return true;
        }

        return false;
    }

    private void InvalidateJammedMember(EntityUid target)
    {
        if (TryComp<CompanyComponent>(target, out var company))
            _factionMembersCache.Remove(company.CompanyName);

        _memberDataCache.Remove(target);
    }

    private void OnJammerStuck(Entity<JamOverwatchOnStuckComponent> ent, ref EntityStuckEvent args)
    {
        InvalidateJammedMember(args.Target);
        if (!TryComp<RatOverwatchCameraComponent>(args.Target, out var camera))
            return;

        // StopWatching removes viewers from the original set. Snapshot only on attachment.
        var watchers = new EntityUid[camera.Watching.Count];
        camera.Watching.CopyTo(watchers);
        foreach (var watcher in watchers)
        {
            if (!Deleted(watcher) && TryComp<RatOverwatchWatchingComponent>(watcher, out var watching))
                StopWatching(watcher, watching);
        }
    }

    private void OnJammerUnstuck(Entity<JamOverwatchOnStuckComponent> ent, ref EntityUnstuckEvent args)
    {
        InvalidateJammedMember(args.Target);
    }

    private void OnJammerShutdown(Entity<JamOverwatchOnStuckComponent> ent, ref ComponentShutdown args)
    {
        if (_jammerStickyQuery.TryGetComponent(ent, out var sticky) && sticky.StuckTo is { } target)
            InvalidateJammedMember(target);
    }
}
