using Content.Client._Exodus.Visuals;
using Content.Shared._Exodus.Virology.Intelligent;
using Robust.Shared.Physics.Components;

namespace Content.Client._Exodus.Virology.Intelligent;

public sealed partial class RotRootVisualsSystem : EntitySystem
{
    private EntityQuery<RotIntelligentComponent> _brains;
    private EntityQuery<PhysicsComponent> _physics;

    public override void Initialize()
    {
        base.Initialize();
        _brains = GetEntityQuery<RotIntelligentComponent>();
        _physics = GetEntityQuery<PhysicsComponent>();
        SubscribeLocalEvent<RotRootVisualsComponent, CreatureAnimationStateEvent>(OnAnimation);
    }

    private void OnAnimation(Entity<RotRootVisualsComponent> ent, ref CreatureAnimationStateEvent args)
    {
        if (args.Dead || !_brains.TryComp(ent, out var brain))
            return;
        if (brain.ChangingForm)
            args.State = brain.Rooted ? ent.Comp.Uprooting : ent.Comp.Rooting;
        else if (!brain.Rooted)
            args.State = _physics.TryComp(ent, out var physics) && physics.LinearVelocity.LengthSquared() > 0.01f
                ? ent.Comp.MobileMoving : ent.Comp.MobileIdle;
    }
}
