using Content.Shared.Interaction.Events;

namespace Content.Shared._Exodus.Virology.Intelligent;

/// <summary>The organic controller may use its own interfaces and actions, never ordinary equipment.</summary>
public sealed class RotControlSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RotIntelligentComponent, InteractionAttemptEvent>(OnInteraction);
        SubscribeLocalEvent<RotIntelligentEyeComponent, InteractionAttemptEvent>(OnEyeInteraction);
    }

    private void OnInteraction(Entity<RotIntelligentComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled |= !ent.Comp.Alive || args.Target is { } target && target != ent.Owner;
    }

    private void OnEyeInteraction(Entity<RotIntelligentEyeComponent> ent, ref InteractionAttemptEvent args) => args.Cancelled = true;
}
