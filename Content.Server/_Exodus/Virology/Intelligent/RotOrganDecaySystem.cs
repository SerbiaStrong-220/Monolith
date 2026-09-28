using Content.Shared.Destructible;
using Robust.Shared.Containers;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotOrganDecaySystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RotOrganDecayComponent, DestructionEventArgs>(OnDestroyed);
    }

    private void OnDestroyed(Entity<RotOrganDecayComponent> ent, ref DestructionEventArgs args) => Play(ent);

    public void Play(EntityUid uid)
    {
        if (!TryComp<RotOrganDecayComponent>(uid, out var comp) || comp.Played || _containers.IsEntityInContainer(uid))
            return;
        comp.Played = true;
        var effect = Spawn(comp.Effect, Transform(uid).Coordinates);
        _transform.SetLocalRotation(effect, Transform(uid).LocalRotation);
    }
}
