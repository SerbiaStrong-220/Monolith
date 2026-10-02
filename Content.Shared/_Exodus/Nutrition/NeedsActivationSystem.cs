using Content.Shared.Mind.Components;
using Robust.Shared.Network;

namespace Content.Shared._Exodus.Nutrition;

/// <summary>Activates a configured body's needs once, without depending on its current connection.</summary>
public sealed partial class NeedsActivationSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    private EntityQuery<NeedsActivationComponent> _activationQuery;

    public override void Initialize()
    {
        base.Initialize();
        _activationQuery = GetEntityQuery<NeedsActivationComponent>();
        SubscribeLocalEvent<NeedsActivationComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NeedsActivationComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NeedsActivationComponent, MindAddedMessage>(OnMindAdded);
    }

    public bool AreNeedsActive(EntityUid uid)
    {
        return !_activationQuery.TryComp(uid, out var activation) || activation.Activated;
    }

    private void OnStartup(Entity<NeedsActivationComponent> ent, ref ComponentStartup args)
    {
        if (_net.IsClient)
            return;

        if (!ent.Comp.Activated && TryComp<MindContainerComponent>(ent, out var mind) && mind.HasMind)
        {
            ent.Comp.Activated = true;
            Dirty(ent);
        }

        var ev = new NeedsActivationChangedEvent();
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnMindAdded(Entity<NeedsActivationComponent> ent, ref MindAddedMessage args)
    {
        if (_net.IsClient || ent.Comp.Activated)
            return;

        ent.Comp.Activated = true;
        Dirty(ent);
        var ev = new NeedsActivationChangedEvent();
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnShutdown(Entity<NeedsActivationComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsClient || ent.Comp.Activated || TerminatingOrDeleted(ent))
            return;

        // Removing the gate from a living body must restore its metabolic rates too.
        ent.Comp.Activated = true;
        var ev = new NeedsActivationChangedEvent();
        RaiseLocalEvent(ent, ref ev);
    }
}
