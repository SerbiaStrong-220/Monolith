using Content.Shared._Mono.MAWC.Shields;
using Content.Shared._Mono.PersonalShield;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.PersonalShield;

/// <summary>
/// Maintains upstream shield links at a bounded rate and replicates membership only when it changes.
/// </summary>
public sealed class ShieldLinkSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<ShieldLinkSourceComponent> _sourceQuery;
    private EntityQuery<ShieldLinkReceiverComponent> _receiverQuery;
    private EntityQuery<PersonalShieldComponent> _shieldQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        base.Initialize();
        _sourceQuery = GetEntityQuery<ShieldLinkSourceComponent>();
        _receiverQuery = GetEntityQuery<ShieldLinkReceiverComponent>();
        _shieldQuery = GetEntityQuery<PersonalShieldComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();
        SubscribeLocalEvent<ShieldLinkSourceComponent, MapInitEvent>(OnSourceMapInit);
        SubscribeLocalEvent<ShieldLinkSourceComponent, ComponentShutdown>(OnSourceShutdown);
        SubscribeLocalEvent<ShieldLinkReceiverComponent, ComponentShutdown>(OnReceiverShutdown);
        SubscribeLocalEvent<ShieldLinkSourceComponent, GotUnequippedEvent>(OnSourceUnequipped);
        SubscribeLocalEvent<ShieldLinkReceiverComponent, GotUnequippedEvent>(OnReceiverUnequipped);
    }

    private void OnSourceMapInit(Entity<ShieldLinkSourceComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextLinkUpdate = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var sources = EntityQueryEnumerator<ShieldLinkSourceComponent, ItemToggleComponent, TransformComponent>();
        while (sources.MoveNext(out var uid, out var source, out var toggle, out var xform))
        {
            if (now < source.NextLinkUpdate)
                continue;

            var interval = source.LinkUpdateInterval > TimeSpan.Zero
                ? source.LinkUpdateInterval
                : TimeSpan.FromSeconds(0.25);
            source.NextLinkUpdate += interval;
            // Coalesce missed updates after a long frame instead of repeatedly rebuilding the same links.
            if (source.NextLinkUpdate <= now)
                source.NextLinkUpdate = now + interval;

            source.DesiredLinks.Clear();
            if (toggle.Activated && source.MaxLinks > 0 && source.MaxRange >= 0f &&
                _shieldQuery.TryGetComponent(uid, out var shield) &&
                _inventory.InSlotWithFlags(uid, shield.Shield.RequiredSlot))
            {
                // Keep valid existing receivers first so iteration order cannot churn full networks.
                foreach (var receiver in source.LinkedShields)
                {
                    if (source.DesiredLinks.Count >= source.MaxLinks)
                        break;
                    if (CanLink((uid, source), xform, receiver))
                        source.DesiredLinks.Add(receiver);
                }

                if (source.DesiredLinks.Count < source.MaxLinks)
                {
                    var receivers = EntityQueryEnumerator<ShieldLinkReceiverComponent>();
                    while (receivers.MoveNext(out var receiver, out _))
                    {
                        if (CanLink((uid, source), xform, receiver))
                            source.DesiredLinks.Add(receiver);
                        if (source.DesiredLinks.Count >= source.MaxLinks)
                            break;
                    }
                }
            }

            ApplyLinks((uid, source));
        }
    }

    private bool CanLink(Entity<ShieldLinkSourceComponent> source, TransformComponent sourceTransform, EntityUid uid)
    {
        return uid != source.Owner && _receiverQuery.TryGetComponent(uid, out var receiver) &&
               receiver.LifeStage < ComponentLifeStage.Stopping && source.Comp.LinkId == receiver.LinkId &&
               _shieldQuery.TryGetComponent(uid, out var shield) &&
               _transformQuery.TryGetComponent(uid, out var xform) &&
               _inventory.InSlotWithFlags(uid, shield.Shield.RequiredSlot) &&
               _transform.InRange(sourceTransform.Coordinates, xform.Coordinates, source.Comp.MaxRange);
    }

    private void ApplyLinks(Entity<ShieldLinkSourceComponent> source)
    {
        foreach (var uid in source.Comp.LinkedShields)
        {
            if (!source.Comp.DesiredLinks.Contains(uid) && _receiverQuery.TryGetComponent(uid, out var receiver) &&
                receiver.LifeStage < ComponentLifeStage.Stopping && receiver.LinkedSources.Remove(source.Owner))
            {
                Dirty(uid, receiver);
                _toggle.TrySetActive(uid, receiver.LinkedSources.Count > 0, predicted: false);
            }
        }

        foreach (var uid in source.Comp.DesiredLinks)
        {
            if (!_receiverQuery.TryGetComponent(uid, out var receiver))
                continue;

            if (receiver.LinkedSources.Add(source.Owner))
                Dirty(uid, receiver);
            // Retry after a broken receiver's cooldown, even when its membership is unchanged.
            _toggle.TrySetActive(uid, true, predicted: false);
        }

        if (source.Comp.LinkedShields.SetEquals(source.Comp.DesiredLinks))
            return;

        source.Comp.LinkedShields.Clear();
        source.Comp.LinkedShields.UnionWith(source.Comp.DesiredLinks);
        if (source.Comp.LifeStage < ComponentLifeStage.Stopping)
            Dirty(source);
    }

    private void ClearSource(Entity<ShieldLinkSourceComponent> source)
    {
        source.Comp.DesiredLinks.Clear();
        ApplyLinks(source);
    }

    private void ClearReceiver(Entity<ShieldLinkReceiverComponent> receiver)
    {
        foreach (var uid in receiver.Comp.LinkedSources)
        {
            if (!_sourceQuery.TryGetComponent(uid, out var source) || !source.LinkedShields.Remove(receiver.Owner))
                continue;

            source.DesiredLinks.Remove(receiver.Owner);
            if (source.LifeStage < ComponentLifeStage.Stopping)
                Dirty(uid, source);
        }

        if (receiver.Comp.LinkedSources.Count == 0)
            return;

        receiver.Comp.LinkedSources.Clear();
        if (receiver.Comp.LifeStage < ComponentLifeStage.Stopping)
        {
            Dirty(receiver);
            _toggle.TrySetActive(receiver.Owner, false, predicted: false);
        }
    }

    private void OnSourceShutdown(Entity<ShieldLinkSourceComponent> ent, ref ComponentShutdown args)
    {
        ClearSource(ent);
    }

    private void OnReceiverShutdown(Entity<ShieldLinkReceiverComponent> ent, ref ComponentShutdown args)
    {
        ClearReceiver(ent);
    }

    private void OnSourceUnequipped(Entity<ShieldLinkSourceComponent> ent, ref GotUnequippedEvent args)
    {
        ClearSource(ent);
    }

    private void OnReceiverUnequipped(Entity<ShieldLinkReceiverComponent> ent, ref GotUnequippedEvent args)
    {
        ClearReceiver(ent);
    }
}
