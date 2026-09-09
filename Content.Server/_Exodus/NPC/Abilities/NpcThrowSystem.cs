using Content.Server.Explosion.EntitySystems;
using Content.Shared._Exodus.NPC.Abilities;
using Content.Shared.Actions;
using Content.Shared.Throwing;
using Robust.Shared.Random;

namespace Content.Server._Exodus.NPC.Abilities;

public sealed partial class NpcThrowSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcThrownWeaponComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NpcThrownWeaponComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NpcThrownWeaponComponent, NpcThrowActionEvent>(OnThrow);
    }

    private void OnMapInit(Entity<NpcThrownWeaponComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.SetUseDelay(ent.Comp.ActionEntity, ent.Comp.Cooldown);
    }

    private void OnShutdown(Entity<NpcThrownWeaponComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnThrow(Entity<NpcThrownWeaponComponent> ent, ref NpcThrowActionEvent args)
    {
        if (args.Handled || Deleted(args.Target))
            return;

        var comp = ent.Comp;
        var ownerPos = _xform.GetMapCoordinates(ent.Owner);
        var targetPos = _xform.GetMapCoordinates(args.Target);

        if (ownerPos.MapId != targetPos.MapId)
            return;

        args.Handled = true;

        // Ready aim fire happens here.
        var toTarget = targetPos.Position - ownerPos.Position;
        var length = toTarget.Length();
        var dist = MathF.Min(length, comp.MaxRange);
        var aim = length <= 0f ? ownerPos.Position : ownerPos.Position + toTarget / length * dist;
        var dir = aim + _random.NextVector2(comp.Spread) - ownerPos.Position;

        var thrown = Spawn(comp.Proto, Transform(ent.Owner).Coordinates);

        if (comp.Prime)
            _trigger.StartTimer((thrown, null), ent.Owner);

        _throwing.TryThrow(thrown, dir, comp.Speed, ent.Owner);
    }
}
