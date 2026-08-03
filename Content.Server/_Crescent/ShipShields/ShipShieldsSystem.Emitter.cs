using Content.Server._Crescent.ShipShields.Components;
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
using System.Diagnostics.CodeAnalysis; // Exodus
using Robust.Shared.Prototypes;

namespace Content.Server._Crescent.ShipShields;

public partial class ShipShieldsSystem
{
    private const float MAX_EMP_DAMAGE = 10000f;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private EntityLookupSystem _lookup = default!; // Exodus
    public void InitializeEmitters()
    {
        SubscribeLocalEvent<ShipShieldEmitterComponent, ShieldDeflectedEvent>(OnShieldDeflected);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentStartup>(OnEmitterStartup); // Exodus fire-control event-driven UI updates
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentRemove>(OnRemoved);
    }

    // Exodus-begin fire-control event-driven UI updates
    private void OnEmitterStartup(Entity<ShipShieldEmitterComponent> owner, ref ComponentStartup args)
    {
        RaiseShieldStateChanged(Transform(owner).GridUid);
    }
    // Exodus-end

    private void OnRemoved(Entity<ShipShieldEmitterComponent> owner, ref ComponentRemove remove)
    {
        var parent = Transform(owner.Owner).GridUid;
        if (parent is null)
            return;
        UnshieldEntity(parent.Value, null);
        RaiseShieldStateChanged(parent); // Exodus fire-control event-driven UI updates
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

        RaiseShieldStateChanged(Transform(uid).GridUid); // Exodus fire-control event-driven UI updates

        QueueDel(args.Deflected);
    }

    private void OnExamined(EntityUid uid, ShipShieldEmitterComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("shield-emitter-examine", ("basedraw", component.BaseDraw), ("additional", CalculateLoadDamage(component))));
        if (HasComp<ShipShieldDisabledGridComponent>(Transform(uid).GridUid))
            args.PushMarkup(Loc.GetString("shield-emitter-examine-invalid-grid"));
    }

    public static float CalculateLoadDamage(ShipShieldEmitterComponent emitter) // Exodus: make public
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
        var ents = new HashSet<Entity<ShipShieldEmitterComponent>>();
        _lookup.GetGridEntities(grid, ents);

        if (ents.Count < 1)
            return false;

        // Exodus-shield-swap-fix-start: report the most representative shield.
        // Prefer the overloaded shield (it carries the recovery timer), then the most loaded one, so
        // the gunnery console can't show an idle spare generator as "online" while the real shield is
        // overloaded and down.
        Entity<ShipShieldEmitterComponent>? best = null;
        foreach (var ent in ents)
        {
            if (best is not { } current
                || ent.Comp.OverloadAccumulator > current.Comp.OverloadAccumulator
                || (ent.Comp.OverloadAccumulator == current.Comp.OverloadAccumulator
                    && CalculateLoadDamage(ent.Comp) > CalculateLoadDamage(current.Comp)))
            {
                best = ent;
            }
        }

        emitter = best!.Value.Owner;
        emitterComp = best.Value.Comp;
        // Exodus-shield-swap-fix-end
        return true;
    }

    public ShipShieldState? GetShieldState(EntityUid ship)
    {
        if (!TryGetShieldEmitter(ship, out var emitterUid, out var emitter))
            return null;

        var powered = TryComp<ApcPowerReceiverComponent>(emitterUid.Value, out var power) && power.Powered;

        return new(
            emitter.BaseDraw,
            CalculateLoadDamage(emitter),
            emitter.MaxDraw,
            emitter.Recharging,
            emitter.OverloadAccumulator,
            CalculateShieldHealth(emitter),
            emitter.Shield is not null,
            powered);
    }

    /// <summary>
    /// Returns the remaining operational health of the shield. The effective capacity is the first
    /// damage-induced shutdown threshold: either the hard damage limit or the maximum extra draw.
    /// </summary>
    private static float CalculateShieldHealth(ShipShieldEmitterComponent emitter)
    {
        if (emitter.DamageLimit <= 0f || emitter.MaxDraw <= 0f)
            return 0f;

        var effectiveDamageLimit = emitter.DamageLimit;
        if (emitter.PowerModifier > 0f && emitter.DamageExp > 0f)
        {
            var maxDrawDamage = MathF.Pow(emitter.MaxDraw / emitter.PowerModifier, 1f / emitter.DamageExp);
            effectiveDamageLimit = MathF.Min(effectiveDamageLimit, maxDrawDamage);
        }

        return Math.Clamp(1f - emitter.Damage / effectiveDamageLimit, 0f, 1f);
    }
    // Exodus-End
}
