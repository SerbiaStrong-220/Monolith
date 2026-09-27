using Content.Server._Exodus.Territory;
using Content.Server.Implants;
using Content.Server.Popups;
using Content.Server.Radio;
using Content.Shared._Exodus.MedicalTracking;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.MedicalTracking;

public sealed partial class MedicalTrackingSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SubdermalImplantSystem _implants = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private GridTerritorySystem _territory = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<MedicalTrackingImplantComponent> _implantQuery;
    private EntityQuery<MedicalTrackingBrainComponent> _brainQuery;

    public override void Initialize()
    {
        base.Initialize();
        _implantQuery = GetEntityQuery<MedicalTrackingImplantComponent>();
        _brainQuery = GetEntityQuery<MedicalTrackingBrainComponent>();
        SubscribeLocalEvent<BodyComponent, AddImplantAttemptEvent>(OnImplantAttempt);
        SubscribeLocalEvent<MedicalTrackingImplantComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<MedicalTrackingImplantComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<MedicalTrackingImplantComponent, ComponentShutdown>(OnImplantShutdown);
        SubscribeLocalEvent<MedicalTrackingImplantComponent, RadioSendAttemptEvent>(OnRadioSend);
        SubscribeLocalEvent<MedicalTrackingBodyComponent, ComponentShutdown>(OnBodyShutdown);
        SubscribeLocalEvent<MedicalTrackingBrainComponent, ComponentShutdown>(OnBrainShutdown);
        InitializeBrainRegistration();
        InitializeVisibility();
        InitializeTablets();
        InitializePinpointers();
    }

    private void OnImplantAttempt(Entity<BodyComponent> ent, ref AddImplantAttemptEvent args)
    {
        if (args.Cancelled || !_implantQuery.TryGetComponent(args.Implant, out var implant))
            return;

        if (TryComp<MedicalTrackingBodyComponent>(ent, out var current) &&
            _implantQuery.TryGetComponent(current.Implant, out var installed) && installed.Tier >= implant.Tier)
        {
            args.Cancel();
            _popup.PopupEntity(Loc.GetString("medical-tracking-implant-no-upgrade"), ent, args.User);
        }
        else if (implant.TrackBrain && FindBrain(ent.AsNullable()) == null)
        {
            args.Cancel();
            _popup.PopupEntity(Loc.GetString("medical-tracking-implant-no-brain"), ent, args.User);
        }
    }

    private EntityUid? FindBrain(Entity<BodyComponent?> body)
    {
        foreach (var (uid, _) in _body.GetBodyOrgans(body.Owner, body.Comp))
        {
            if (_brainOrganQuery.HasComp(uid) && !TerminatingOrDeleted(uid))
                return uid;
        }

        return null;
    }

    private void OnImplanted(Entity<MedicalTrackingImplantComponent> ent, ref ImplantImplantedEvent args)
    {
        if (args.Implanted is not { } body || TerminatingOrDeleted(body))
            return;

        if (TryComp<MedicalTrackingBodyComponent>(body, out var previous) && previous.Implant != ent.Owner &&
            _implantQuery.TryGetComponent(previous.Implant, out var old))
        {
            if (old.Tier >= ent.Comp.Tier)
            {
                // Container insertion is still dispatching events; let it finish before removing this implant.
                QueueDel(ent);
                return;
            }

            _implants.ForceRemove(body, previous.Implant);
        }

        var trackedBody = EnsureComp<MedicalTrackingBodyComponent>(body);
        trackedBody.Implant = ent.Owner;
        ent.Comp.Body = body;
        ent.Comp.NextUpdate = _timing.CurTime;
        UpdateHudBorder(ent, body);

        UpdateBrainRegistration(ent, body);
    }

    private void OnRemoved(Entity<MedicalTrackingImplantComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (args.Container.Owner == ent.Comp.Body)
            Unregister(ent);
    }

    private void OnImplantShutdown(Entity<MedicalTrackingImplantComponent> ent, ref ComponentShutdown args)
    {
        Unregister(ent);
    }

    private void Unregister(Entity<MedicalTrackingImplantComponent> ent)
    {
        // A brain keeps its registration when the body is destroyed, but not when its implant is extracted.
        if (ent.Comp.Brain is { } brain && _brainQuery.TryGetComponent(brain, out var registration) &&
            registration.Implant == ent.Owner)
        {
            if (ent.Comp.Body is { } owner && !TerminatingOrDeleted(owner))
                registration.Registered = false;
            registration.Implant = null;
        }

        if (ent.Comp.Body is { } body && !TerminatingOrDeleted(body))
        {
            if (TryComp<MedicalTrackingBodyComponent>(body, out var tracked) && tracked.Implant == ent.Owner)
            {
                tracked.Implant = EntityUid.Invalid;
                RemCompDeferred<MedicalTrackingHudComponent>(body);
            }
        }

        ent.Comp.Body = null;
        ent.Comp.Brain = null;
        ent.Comp.Contact = null;
    }

    private void OnBodyShutdown(Entity<MedicalTrackingBodyComponent> ent, ref ComponentShutdown args)
    {
        if (_implantQuery.TryGetComponent(ent.Comp.Implant, out var implant) && implant.Body == ent.Owner)
            Unregister((ent.Comp.Implant, implant));
    }

    private void OnBrainShutdown(Entity<MedicalTrackingBrainComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Implant is { } uid && _implantQuery.TryGetComponent(uid, out var implant) &&
            implant.Brain == ent.Owner)
            implant.Brain = null;
    }

    private void OnRadioSend(Entity<MedicalTrackingImplantComponent> ent, ref RadioSendAttemptEvent args)
    {
        if (ent.Comp.Body == null || (ent.Comp.NotificationTerritory is { } territory &&
            !_territory.IsInTerritory(ent.Owner, territory)))
            args.Cancelled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MedicalTrackingBodyComponent, MobStateComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var tracking, out var state, out var transform, out var metadata))
        {
            if (!metadata.EntityInitialized || metadata.EntityLifeStage >= EntityLifeStage.Terminating ||
                state.CurrentState == MobState.Invalid)
                continue;

            if (!tracking.Implant.Valid)
            {
                RemCompDeferred<MedicalTrackingBodyComponent>(uid);
                continue;
            }

            if (!_implantQuery.TryGetComponent(tracking.Implant, out var implant) ||
                (!implant.TrackBody && !implant.TrackBrain) ||
                implant.Body != uid || now < implant.NextUpdate)
                continue;

            // Advance by whole intervals after a stall; never resample repeatedly to catch up.
            var interval = implant.UpdateInterval > TimeSpan.Zero ? implant.UpdateInterval : TimeSpan.FromSeconds(5);
            implant.NextUpdate += interval * (1 + (now - implant.NextUpdate).Ticks / interval.Ticks);
            // Job implants may be inserted before the brain is available. Retry on the normal sampling interval.
            UpdateBrainRegistration((tracking.Implant, implant), uid);
            if (!implant.TrackBody)
                continue;

            implant.Contact = transform.MapID == MapId.Nullspace || !CanTrackBody((uid, state)) ? null : new MedicalTrackingContact(
                GetNetEntity(uid), Identity.Name(uid, EntityManager), implant.TierName,
                new MapCoordinates(_transform.GetWorldPosition(transform), transform.MapID),
                state.CurrentState, now, implant.TierColor);
        }

        UpdateTablets(now);
        UpdatePinpointers(now);
    }
}
