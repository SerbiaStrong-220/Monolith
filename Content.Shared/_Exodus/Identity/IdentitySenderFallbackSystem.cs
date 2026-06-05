using Content.Shared.Access.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.StationAi;

namespace Content.Shared._Exodus.Identity;

/// <summary>
/// Provides a fallback sender title for devices carrying <see cref="IdentitySenderFallbackComponent"/>
/// when no other system resolved the actor's title. Runs after the standard title providers so it
/// only fills the gap (e.g. an Asakim without an ID card on a biocoded comms console).
/// </summary>
public sealed class IdentitySenderFallbackSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TryGetIdentityShortInfoEvent>(OnTryGetIdentity,
            after: [typeof(SharedIdCardSystem), typeof(SharedBorgSystem), typeof(SharedStationAiSystem)]);
    }

    private void OnTryGetIdentity(TryGetIdentityShortInfoEvent ev)
    {
        if (ev.Title != null)
            return;

        if (ev.WhileInteractingWith is not { } device)
            return;

        if (!TryComp<IdentitySenderFallbackComponent>(device, out var fallback))
            return;

        ev.Title = Loc.GetString(fallback.Fallback, ("user", IdentityManagement.Identity.Name(ev.ForActor, EntityManager)));
    }
}
