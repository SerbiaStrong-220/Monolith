using Content.Server._NF.Trade;
using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.BoxSorter;
using Content.Shared._NF.Trade;
using Content.Shared.Conveyor;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Events;

namespace Content.Server._Exodus.BoxSorter;

public sealed partial class BoxSorterSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    private const string SorterFixture = "sorter";

    private EntityQuery<CargoBoxTeleporterComponent> _teleporterQuery = default!;
    private EntityQuery<TradeCrateComponent> _tradeQuery = default!;
    private EntityQuery<TradeCrateDestinationComponent> _destinationQuery = default!;
    private EntityQuery<MobStateComponent> _mobQuery = default!;

    private readonly Dictionary<(EntityUid Grid, int Channel), HashSet<EntityUid>> _padsByGridChannel = new();
    private readonly Dictionary<EntityUid, (EntityUid Grid, int Channel)> _padIndex = new();

    public override void Initialize()
    {
        base.Initialize();

        _teleporterQuery = GetEntityQuery<CargoBoxTeleporterComponent>();
        _tradeQuery = GetEntityQuery<TradeCrateComponent>();
        _destinationQuery = GetEntityQuery<TradeCrateDestinationComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();

        SubscribeLocalEvent<BoxSorterComponent, StartCollideEvent>(OnCollide);
        SubscribeLocalEvent<BoxSorterComponent, ExaminedEvent>(OnSorterExamined);
        SubscribeLocalEvent<CargoBoxTeleporterComponent, ExaminedEvent>(OnPadExamined);
        SubscribeLocalEvent<CargoBoxTeleporterComponent, ComponentStartup>(OnPadStartup);
        SubscribeLocalEvent<CargoBoxTeleporterComponent, ComponentShutdown>(OnPadShutdown);
        SubscribeLocalEvent<CargoBoxTeleporterComponent, EntParentChangedMessage>(OnPadParentChanged);
        SubscribeLocalEvent<CargoTeleporterFrameComponent, AfterInteractEvent>(OnFrameInteract);
        SubscribeLocalEvent<CargoTeleporterFrameComponent, BoxSorterDeployDoAfterEvent>(OnFrameDeployDoAfter);

        Subs.BuiEvents<BoxSorterComponent>(BoxSorterUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<BoxSorterSetOtherRouteMessage>(OnSetOtherRoute);
            subs.Event<BoxSorterSetDestinationRouteMessage>(OnSetDestinationRoute);
        });

        Subs.BuiEvents<CargoBoxTeleporterComponent>(CargoBoxTeleporterUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnPadUiOpened);
            subs.Event<CargoBoxTeleporterSetChannelMessage>(OnPadSetChannel);
        });
    }

    private void OnCollide(Entity<BoxSorterComponent> ent, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != SorterFixture)
            return;

        var subject = args.OtherEntity;
        if (TerminatingOrDeleted(subject) || TerminatingOrDeleted(ent.Owner))
            return;

        if (!Transform(ent.Owner).Anchored)
            return;

        if (!_power.IsPowered(ent.Owner))
            return;

        if (Transform(subject).Anchored)
            return;

        if (!_tradeQuery.TryComp(subject, out var trade))
            return;

        int? channel = null;

        if (_destinationQuery.TryComp(trade.DestinationStation, out var destination)
            && ent.Comp.DestinationRoutes.TryGetValue(destination.DestinationProto.Id, out var destChannel))
        {
            channel = destChannel;
        }

        channel ??= ent.Comp.RouteOther;

        if (channel == null)
            return;

        if (!TryGetPadByChannel(ent.Owner, channel.Value, out var target))
            return;

        if (HasMobInside(subject))
            return;

        BreakPulls(subject);

        _transform.SetCoordinates(subject, Transform(target).Coordinates);

        _audio.PlayPvs(ent.Comp.TeleportSound, ent.Owner);
        _audio.PlayPvs(ent.Comp.TeleportSound, target);
    }

    private bool TryGetPadByChannel(EntityUid sorter, int channel, out EntityUid pad)
    {
        pad = EntityUid.Invalid;

        var sorterGrid = Transform(sorter).GridUid;
        if (sorterGrid == null)
            return false;

        if (!_padsByGridChannel.TryGetValue((sorterGrid.Value, channel), out var candidates))
            return false;

        var sorterCoords = _transform.GetMapCoordinates(sorter);
        var bestDistance = float.MaxValue;
        var found = false;

        foreach (var uid in candidates)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (!_teleporterQuery.TryComp(uid, out var teleporter) || teleporter.Channel != channel)
                continue;

            var xform = Transform(uid);
            if (xform.GridUid != sorterGrid)
                continue;

            if (!_power.IsPowered(uid))
                continue;

            var padCoords = _transform.GetMapCoordinates(uid);
            var distance = (float) (padCoords.Position - sorterCoords.Position).LengthSquared();
            if (found && distance >= bestDistance)
                continue;

            bestDistance = distance;
            pad = uid;
            found = true;
        }

        return found;
    }

    private void IndexPad(Entity<CargoBoxTeleporterComponent> ent)
    {
        var (uid, comp) = ent;
        DeindexPad(uid);

        var grid = Transform(uid).GridUid;
        if (grid == null)
            return;

        var key = (grid.Value, comp.Channel);
        _padIndex[uid] = key;
        if (!_padsByGridChannel.TryGetValue(key, out var set))
        {
            set = new HashSet<EntityUid>();
            _padsByGridChannel[key] = set;
        }
        set.Add(uid);
    }

    private void DeindexPad(EntityUid uid)
    {
        if (!_padIndex.Remove(uid, out var key))
            return;

        if (_padsByGridChannel.TryGetValue(key, out var set))
        {
            set.Remove(uid);
            if (set.Count == 0)
                _padsByGridChannel.Remove(key);
        }
    }

    private bool HasMobInside(EntityUid uid)
    {
        foreach (var container in _container.GetAllContainers(uid))
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (_mobQuery.HasComp(contained))
                    return true;
            }
        }

        return false;
    }

    private void BreakPulls(EntityUid uid)
    {
        if (TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(uid, pullable);

        if (TryComp<PullerComponent>(uid, out var puller) && puller.Pulling != null)
        {
            if (TryComp<PullableComponent>(puller.Pulling.Value, out var pulled))
                _pulling.TryStopPull(puller.Pulling.Value, pulled);
        }
    }

    private void OnUiOpened(Entity<BoxSorterComponent> ent, ref BoundUIOpenedEvent args)
    {
        _ui.SetUiState(ent.Owner, BoxSorterUiKey.Key, BuildState(ent.Comp));
    }

    private void OnSetOtherRoute(Entity<BoxSorterComponent> ent, ref BoxSorterSetOtherRouteMessage msg)
    {
        if (!Exists(msg.Actor))
            return;

        if (TerminatingOrDeleted(ent.Owner))
            return;

        if (msg.Channel != null && !BoxSorterComponent.IsValidChannel(msg.Channel.Value))
            return;

        ent.Comp.RouteOther = msg.Channel;

        Dirty(ent.Owner, ent.Comp);
        _ui.SetUiState(ent.Owner, BoxSorterUiKey.Key, BuildState(ent.Comp));
    }

    private void OnSetDestinationRoute(Entity<BoxSorterComponent> ent, ref BoxSorterSetDestinationRouteMessage msg)
    {
        if (!Exists(msg.Actor))
            return;

        if (TerminatingOrDeleted(ent.Owner))
            return;

        if (string.IsNullOrEmpty(msg.Destination))
            return;

        if (msg.Channel == null)
        {
            if (ent.Comp.DestinationRoutes.Remove(msg.Destination))
                Dirty(ent.Owner, ent.Comp);
        }
        else
        {
            if (!BoxSorterComponent.IsValidChannel(msg.Channel.Value))
                return;

            ent.Comp.DestinationRoutes[msg.Destination] = msg.Channel.Value;
            Dirty(ent.Owner, ent.Comp);
        }

        _ui.SetUiState(ent.Owner, BoxSorterUiKey.Key, BuildState(ent.Comp));
    }

    private void OnPadUiOpened(Entity<CargoBoxTeleporterComponent> ent, ref BoundUIOpenedEvent args)
    {
        _ui.SetUiState(ent.Owner, CargoBoxTeleporterUiKey.Key, new CargoBoxTeleporterUiState(ent.Comp.Channel));
    }

    private void OnPadSetChannel(Entity<CargoBoxTeleporterComponent> ent, ref CargoBoxTeleporterSetChannelMessage msg)
    {
        if (!Exists(msg.Actor))
            return;

        if (TerminatingOrDeleted(ent.Owner))
            return;

        if (!BoxSorterComponent.IsValidChannel(msg.Channel))
            return;

        ent.Comp.Channel = msg.Channel;
        Dirty(ent.Owner, ent.Comp);
        UpdatePadVisual(ent);
        IndexPad(ent);
        _ui.SetUiState(ent.Owner, CargoBoxTeleporterUiKey.Key, new CargoBoxTeleporterUiState(ent.Comp.Channel));
    }

    private void OnPadStartup(Entity<CargoBoxTeleporterComponent> ent, ref ComponentStartup args)
    {
        UpdatePadVisual(ent);
        IndexPad(ent);
    }

    private void OnPadShutdown(Entity<CargoBoxTeleporterComponent> ent, ref ComponentShutdown args)
    {
        DeindexPad(ent.Owner);
    }

    private void OnPadParentChanged(Entity<CargoBoxTeleporterComponent> ent, ref EntParentChangedMessage args)
    {
        IndexPad(ent);
    }

    private void OnFrameInteract(Entity<CargoTeleporterFrameComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        if (args.Target is not { } target)
            return;

        if (!HasComp<ConveyorComponent>(target) || !Transform(target).Anchored)
            return;

        if (!TryComp<HandsComponent>(args.User, out var hands))
            return;

        if (!_hands.TryDrop(args.User, ent.Owner, targetDropLocation: args.ClickLocation, handsComp: hands))
            return;

        var ev = new BoxSorterDeployDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.DeployDelay, ev, ent.Owner, target: target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };
        _doAfter.TryStartDoAfter(doAfter);
        args.Handled = true;
    }

    private void OnFrameDeployDoAfter(Entity<CargoTeleporterFrameComponent> ent, ref BoxSorterDeployDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (args.Args.Target is not { } target)
            return;

        if (TerminatingOrDeleted(ent.Owner) || TerminatingOrDeleted(target))
            return;

        if (!HasComp<ConveyorComponent>(target) || !Transform(target).Anchored)
            return;

        if (Transform(ent.Owner).GridUid != Transform(target).GridUid)
            return;

        var pad = Spawn("CargoBoxTeleporter", Transform(target).Coordinates);
        if (TerminatingOrDeleted(pad))
            return;

        QueueDel(ent.Owner);
        args.Handled = true;
    }

    private void OnSorterExamined(Entity<BoxSorterComponent> ent, ref ExaminedEvent args)
    {
        if (!_power.IsPowered(ent.Owner))
            args.PushMarkup(Loc.GetString("box-sorter-examine-unpowered"));

        var shown = new HashSet<string>();
        var destQuery = EntityQueryEnumerator<TradeCrateDestinationComponent>();
        while (destQuery.MoveNext(out var destUid, out var dest))
        {
            if (TerminatingOrDeleted(destUid))
                continue;

            if (!shown.Add(dest.DestinationProto.Id))
                continue;

            if (!ent.Comp.DestinationRoutes.TryGetValue(dest.DestinationProto.Id, out var channel))
                continue;

            args.PushMarkup(Loc.GetString("box-sorter-examine-route",
                ("dest", Name(destUid)),
                ("channel", channel)));
        }

        var other = ent.Comp.RouteOther?.ToString() ?? Loc.GetString("box-sorter-unconfigured");
        args.PushMarkup(Loc.GetString("box-sorter-examine-route",
            ("dest", Loc.GetString("box-sorter-type-other")),
            ("channel", other)));
    }
    private void UpdatePadVisual(Entity<CargoBoxTeleporterComponent> ent)
    {
        _appearance.SetData(ent.Owner, CargoBoxTeleporterVisuals.Channel, ent.Comp.Channel);
    }

    private void OnPadExamined(Entity<CargoBoxTeleporterComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("cargo-teleporter-examine",
            ("id", ent.Comp.TeleporterId),
            ("channel", ent.Comp.Channel)));
    }

    private BoxSorterUiState BuildState(BoxSorterComponent comp)
    {
        var destinations = new Dictionary<string, string>();
        var destinationRoutes = new Dictionary<string, int>();
        var destQuery = EntityQueryEnumerator<TradeCrateDestinationComponent>();
        while (destQuery.MoveNext(out var destUid, out var dest))
        {
            if (TerminatingOrDeleted(destUid))
                continue;

            var proto = dest.DestinationProto.Id;
            if (!destinations.ContainsKey(proto))
                destinations[proto] = Name(destUid);

            if (comp.DestinationRoutes.TryGetValue(proto, out var channel))
                destinationRoutes[proto] = channel;
        }

        return new BoxSorterUiState(comp.RouteOther, destinations, destinationRoutes);
    }
}
