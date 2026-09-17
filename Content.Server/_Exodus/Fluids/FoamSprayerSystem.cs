using Content.Server.Fluids.EntitySystems;
using Content.Shared._Exodus.Fluids;
using Content.Shared.Charges.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Fluids;

public sealed class FoamSprayerSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedChargesSystem _charges = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    private EntityQuery<HandsComponent> _handsQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _handsQuery = GetEntityQuery<HandsComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();
        SubscribeLocalEvent<FoamSprayerComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<FoamSprayerComponent, FoamSprayerDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<FoamSprayerComponent, DoAfterAttemptEvent<FoamSprayerDoAfterEvent>>(OnDoAfterAttempt);
    }

    private void OnUseInHand(Entity<FoamSprayerComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!CanSpray(ent, args.User, out _))
            return;

        var ev = new FoamSprayerDoAfterEvent
        {
            StartCoordinates = GetNetCoordinates(Transform(args.User).Coordinates),
        };
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, ev, ent, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            MultiplyDelay = false,
            AttemptFrequency = AttemptFrequency.EveryTick,
        });
    }

    private void OnDoAfterAttempt(Entity<FoamSprayerComponent> ent, ref DoAfterAttemptEvent<FoamSprayerDoAfterEvent> args)
    {
        if (!CanContinue(ent, args.Event))
            args.Cancel();
    }

    private bool CanContinue(Entity<FoamSprayerComponent> ent, FoamSprayerDoAfterEvent args)
    {
        // Preparation must remain stationary even for users with interruption exemptions.
        return _handsQuery.TryComp(args.User, out var hands)
            && hands.ActiveHandEntity == ent.Owner
            && _transformQuery.TryComp(args.User, out var xform)
            && _transform.InRange(xform.Coordinates, GetCoordinates(args.StartCoordinates), args.Args.MovementThreshold);
    }

    private void OnDoAfter(Entity<FoamSprayerComponent> ent, ref FoamSprayerDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (!CanContinue(ent, args) || !CanSpray(ent, args.User, out var tile))
            return;

        var foam = Spawn(ent.Comp.FoamPrototype, _turf.GetTileCenter(tile));
        if (!TryComp<SmokeComponent>(foam, out var smoke))
        {
            Log.Error($"Foam prototype {ent.Comp.FoamPrototype} is missing SmokeComponent.");
            QueueDel(foam);
            return;
        }

        if (!_charges.TryUseCharge((ent.Owner, null)))
        {
            QueueDel(foam);
            return;
        }

        // StartSmoke consumes the supplied solution, so preserve the template for later uses.
        _smoke.StartSmoke(foam, ent.Comp.Solution.Clone(), (float) ent.Comp.FoamDuration.TotalSeconds, ent.Comp.SpreadAmount, smoke);
        _audio.PlayPvs(ent.Comp.SpraySound, ent.Owner);
    }

    private bool CanSpray(Entity<FoamSprayerComponent> ent, EntityUid user, out TileRef tile)
    {
        tile = default;
        if (TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent.Owner)
            || TerminatingOrDeleted(user)
            || !_handsQuery.TryComp(user, out var hands) || hands.ActiveHandEntity != ent.Owner)
        {
            return false;
        }

        if (_charges.IsEmpty(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("exodus-foam-sprayer-recharging"), ent.Owner, user);
            return false;
        }

        if (!_turf.TryGetTileRef(Transform(user).Coordinates, out var floor) || floor.Value.Tile.IsEmpty || _turf.IsSpace(floor.Value))
        {
            _popup.PopupEntity(Loc.GetString("exodus-foam-sprayer-needs-floor"), ent.Owner, user);
            return false;
        }

        tile = floor.Value;
        return true;
    }
}
