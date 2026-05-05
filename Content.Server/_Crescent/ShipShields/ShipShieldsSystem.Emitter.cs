using Content.Shared._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Components;
using Content.Server.Emp;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Station.Systems;
using Robust.Shared.Audio.Systems;
using Content.Shared.Examine;
using Content.Server.Explosion.Components;
using Content.Shared.Explosion.Components;
using Content.Shared.Exodus.ShipShields; // Exodus
using System.Linq; // Exodus
using System.Diagnostics.CodeAnalysis; // Exodus
using Robust.Shared.Prototypes;

namespace Content.Server._Crescent.ShipShields;

public partial class ShipShieldsSystem
{
    private const float MAX_EMP_DAMAGE = 10000f;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!; // Exodus

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    public void InitializeEmitters()
    {
        SubscribeLocalEvent<ShipShieldEmitterComponent, ShieldDeflectedEvent>(OnShieldDeflected);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentRemove>(OnRemoved);
    }


    private void OnRemoved(Entity<ShipShieldEmitterComponent> owner, ref ComponentRemove remove)
    {
        var parent = Transform(owner.Owner).GridUid;
        if (parent is null)
            return;
        UnshieldEntity(parent.Value, null);
    }

    private void OnShieldDeflected(EntityUid uid, ShipShieldEmitterComponent component, ShieldDeflectedEvent args)
    {
        if (TryComp<EmpOnTriggerComponent>(args.Deflected, out var emp))
        {
            component.Damage += Math.Clamp(emp.EnergyConsumption, 0f, MAX_EMP_DAMAGE);
            _trigger.Trigger(args.Deflected);
        }

        if (TryComp<ExplosiveComponent>(args.Deflected, out var exp) && _prototypeManager.TryIndex(exp.ExplosionType, out var type))
        {
            component.Damage += exp.TotalIntensity * (float)type.DamagePerIntensity.GetTotal();
        }

        component.Damage += (float)args.Projectile.Damage.GetTotal();
        args.Projectile.ProjectileSpent = true;

        QueueDel(args.Deflected);
    }

    private void OnExamined(EntityUid uid, ShipShieldEmitterComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("shield-emitter-examine", ("basedraw", component.BaseDraw), ("additional", CalculateLoadDamage(component))));
    }

    private static float CalculateLoadDamage(ShipShieldEmitterComponent emitter)
    {
        return (float)Math.Clamp(Math.Pow(emitter.Damage, emitter.DamageExp) * emitter.PowerModifier, 0f, emitter.MaxDraw);
    }

    private void AdjustEmitterLoad(EntityUid uid, ShipShieldEmitterComponent? emitter = null, ApcPowerReceiverComponent? receiver = null)
    {
        if (!Resolve(uid, ref emitter, ref receiver))
            return;

        receiver.Load = emitter.BaseDraw + CalculateLoadDamage(emitter);
    }

    // Exodus-Start | add friendly public api
    public bool TryGetShieldEmitter(EntityUid grid, [NotNullWhen(true)] out EntityUid? emitter, [NotNullWhen(true)] out ShipShieldEmitterComponent? emitterComp)
    {
        emitter = null;
        emitterComp = null;

        if (TryComp<ShipShieldedComponent>(grid, out var shielded)
            && shielded.Source != null
            && TryComp(shielded.Source, out emitterComp))
        {
            emitter = shielded.Source.Value;
            return true;
        }

        // if ship isn't shielded it doesn't means that ship doesn't have shield emitter
        // take the first one you find on grid
        var ents = new HashSet<Entity<ShipShieldEmitterComponent>>();
        _lookup.GetGridEntities(grid, ents);

        if (ents.Count < 1)
            return false;

        var emitterEnt = ents.First();
        emitter = emitterEnt;
        emitterComp = emitterEnt.Comp;
        return true;
    }

    public ShipShieldState? GetShieldState(EntityUid ship)
    {
        if (!TryGetShieldEmitter(ship, out _, out var emitter))
            return null;

        return new(emitter.BaseDraw, CalculateLoadDamage(emitter), emitter.MaxDraw, emitter.Recharging, emitter.OverloadAccumulator);
    }
    // Exodus-End
}
