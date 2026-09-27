using Content.Server.Body.Components;
using Content.Server.IdentityManagement;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.IdentityManagement;

namespace Content.Server._Exodus.MedicalTracking;

public sealed partial class MedicalTrackingSystem
{
    [Dependency] private IdentitySystem _identity = default!;

    private EntityQuery<BrainComponent> _brainOrganQuery;
    private EntityQuery<OrganComponent> _organQuery;
    private EntityQuery<BodyComponent> _bodyQuery;
    private EntityQuery<MedicalTrackingBodyComponent> _trackedBodyQuery;

    private void InitializeBrainRegistration()
    {
        _brainOrganQuery = GetEntityQuery<BrainComponent>();
        _organQuery = GetEntityQuery<OrganComponent>();
        _bodyQuery = GetEntityQuery<BodyComponent>();
        _trackedBodyQuery = GetEntityQuery<MedicalTrackingBodyComponent>();
        // BrainSystem owns this event on BrainComponent; observe organ attachment through OrganComponent.
        SubscribeLocalEvent<OrganComponent, OrganAddedToBodyEvent>(OnOrganAdded);
        SubscribeLocalEvent<MedicalTrackingBodyComponent, IdentityChangedEvent>(OnClientIdentityChanged);
    }

    private void UpdateBrainRegistration(Entity<MedicalTrackingImplantComponent> implant, EntityUid body)
    {
        if (!implant.Comp.TrackBrain || TerminatingOrDeleted(body) || !MetaData(body).EntityInitialized)
            return;

        // Keep tracking the original brain after extraction instead of switching to a replacement organ.
        if (implant.Comp.Brain is { } registered && !TerminatingOrDeleted(registered) &&
            _brainQuery.TryComp(registered, out var registration) && registration.Registered &&
            registration.Implant == implant.Owner)
        {
            registration.TierName = implant.Comp.TierName;
            registration.TierColor = implant.Comp.TierColor;
            return;
        }

        if (_bodyQuery.TryComp(body, out var bodyComp) && FindBrain((body, bodyComp)) is { } brain)
            RegisterBrain(implant, body, brain);
    }

    private void RegisterBrain(Entity<MedicalTrackingImplantComponent> implant, EntityUid body, EntityUid brain)
    {
        var registration = EnsureComp<MedicalTrackingBrainComponent>(brain);
        registration.Implant = implant.Owner;
        registration.Registered = true;
        registration.TierName = implant.Comp.TierName;
        registration.TierColor = implant.Comp.TierColor;
        // Identity.Name reads a cache that still contains "identity" during round-start implantation.
        // Resolve the current visible identity directly, retaining disguise and ID-card rules.
        registration.ClientName = _identity.GetEntityIdentity(body);
        implant.Comp.Brain = brain;
    }

    private void OnOrganAdded(Entity<OrganComponent> ent, ref OrganAddedToBodyEvent args)
    {
        if (!_brainOrganQuery.HasComp(ent.Owner) || TerminatingOrDeleted(ent) || TerminatingOrDeleted(args.Body) ||
            !_trackedBodyQuery.TryComp(args.Body, out var tracked) ||
            !_implantQuery.TryComp(tracked.Implant, out var implant) || !implant.TrackBrain ||
            implant.Body != args.Body || implant.Brain != null)
            return;

        RegisterBrain((tracked.Implant, implant), args.Body, ent.Owner);
    }

    private void OnClientIdentityChanged(Entity<MedicalTrackingBodyComponent> ent, ref IdentityChangedEvent args)
    {
        if (!_implantQuery.TryComp(ent.Comp.Implant, out var implant) || implant.Body != ent.Owner ||
            implant.Brain is not { } brain || !_brainQuery.TryComp(brain, out var registration) ||
            registration.Implant != ent.Comp.Implant || !registration.Registered ||
            !_organQuery.TryComp(brain, out var organ) || organ.Body != ent.Owner)
            return;

        // Finalize spawn-time identity changes, but retain the client's name after brain extraction.
        registration.ClientName = Identity.Name(ent.Owner, EntityManager);
    }
}
