// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Shared.Exodus.Gimmicks.Pheromones;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client.Exodus.Gimmicks.Pheromones;

public sealed partial class PheromonesSystem : SharedPheromonesSystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PheromonesComponent, ComponentStartup>(PheromonesStartup);
        SubscribeLocalEvent<PheromonesComponent, ComponentShutdown>(PheromonesShutdown);
        SubscribeLocalEvent<PheromonesCommunicationComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PheromonesCommunicationComponent, PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<PheromonesCommunicationComponent, PlayerDetachedEvent>(OnDetached);
        SubscribeLocalEvent<PheromonesCommunicationComponent, ComponentShutdown>(OnShutdown);
    }

    public void RefreshPheromonesVisibility()
    {
        var show = CanSeePheromones(_player.LocalEntity);

        var pheromones = EntityQueryEnumerator<PheromonesComponent>();

        while (pheromones.MoveNext(out var uid, out var pheromone))
        {
            if (!TryComp<SpriteComponent>(uid, out var sprite))
                continue;

            pheromone.OldSpriteColor = sprite.Color;

            _sprite.SetColor((uid, sprite), pheromone.Color);

            if (pheromone.Hidden)
                _sprite.SetVisible((uid, sprite), show);
        }
    }

    private void PheromonesStartup(Entity<PheromonesComponent> uid, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        var show = CanSeePheromones(_player.LocalEntity);

        if (uid.Comp.Hidden && !show)
        {
            _sprite.SetVisible((uid, sprite), false);
        }
        if (show)
        {
            uid.Comp.OldSpriteColor = sprite.Color;
            _sprite.SetColor((uid, sprite), uid.Comp.Color);
        }
    }

    private void PheromonesShutdown(Entity<PheromonesComponent> uid, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        if (uid.Comp.Hidden)
        {
            _sprite.SetVisible((uid, sprite), true);
        }

        if (uid.Comp.OldSpriteColor != null)
            _sprite.SetColor((uid, sprite), uid.Comp.OldSpriteColor.Value);
    }

    private void OnMapInit(Entity<PheromonesCommunicationComponent> entity, ref MapInitEvent args)
    {
        if (_player.LocalEntity == entity.Owner)
            RefreshPheromonesVisibility();
    }

    private void OnShutdown(Entity<PheromonesCommunicationComponent> entity, ref ComponentShutdown args)
    {
        if (_player.LocalEntity == entity.Owner)
            RefreshPheromonesVisibility();
    }

    private void OnAttached(Entity<PheromonesCommunicationComponent> entity, ref PlayerAttachedEvent args)
    {
        if (_player.LocalEntity == entity.Owner)
            RefreshPheromonesVisibility();
    }

    private void OnDetached(Entity<PheromonesCommunicationComponent> entity, ref PlayerDetachedEvent args)
    {
        RefreshPheromonesVisibility();
    }
}
