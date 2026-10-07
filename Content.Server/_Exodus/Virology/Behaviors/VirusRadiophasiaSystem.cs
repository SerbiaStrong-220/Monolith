// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Damage;
using Content.Server.Radiation.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Radiation.Components;
using Content.Shared.Radiation.Events;
using Content.Shared._Exodus.Virology.Behaviors;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Behaviors;

public sealed partial class VirusRadiophasiaSystem : EntitySystem
{
    [Dependency] private RadiationSystem _radiation = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan HealingInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VirusRadiophasiaComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<VirusRadiophasiaComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VirusRadiophasiaComponent, DamageModifyEvent>(OnDamageTaken);
        SubscribeLocalEvent<VirusRadiophasiaComponent, OnIrradiatedEvent>(OnIrradiated);
    }

    private void OnStartup(Entity<VirusRadiophasiaComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.StateApplied)
            return;

        ent.Comp.StateApplied = true;
        ent.Comp.NextHealingReset = _timing.CurTime + HealingInterval;

        if (TryComp<RadiationSourceComponent>(ent.Owner, out var existing))
            ent.Comp.PreviousIntensity = existing.Intensity;
        else
            ent.Comp.AddedRadiation = true;

        EnsureComp<RadiationSourceComponent>(ent.Owner);
        _radiation.SetIntensity(ent.Owner, ent.Comp.RadiationIntensity);
    }

    private void OnShutdown(Entity<VirusRadiophasiaComponent> ent, ref ComponentShutdown args)
    {
        if (Terminating(ent.Owner))
            return;

        if (ent.Comp.AddedRadiation)
            RemComp<RadiationSourceComponent>(ent.Owner);
        else if (ent.Comp.PreviousIntensity is { } previous)
            _radiation.SetIntensity(ent.Owner, previous);
    }

    private void OnDamageTaken(Entity<VirusRadiophasiaComponent> ent, ref DamageModifyEvent args)
    {
        if (!args.Damage.DamageDict.TryGetValue("Radiation", out var damageValue) ||
            damageValue <= 0)
            return;

        // Ambient healing is handled by OnIrradiated, but every radiation source receives protection.
        if (args.OriginFlag != DamageableSystem.DamageOriginFlag.Irradiation && !ent.Comp.HealPerDamageUnit.Empty)
            args.Damage += GetHealing(ent, ent.Comp.HealPerDamageUnit, damageValue.Double());
        else
            args.Damage = new DamageSpecifier(args.Damage);

        args.Damage.DamageDict["Radiation"] = damageValue * ent.Comp.RadiationDamageCoefficient;
    }

    private void OnIrradiated(Entity<VirusRadiophasiaComponent> ent, ref OnIrradiatedEvent args)
    {
        if (ent.Comp.HealPerRad.Empty)
            return;

        // host is radiation emitter so this will prevent self-heal
        var externalRads = args.TotalRads - ent.Comp.RadiationIntensity * args.FrameTime;
        if (externalRads <= 0f)
            return;

        var healing = GetHealing(ent, ent.Comp.HealPerRad, externalRads);
        if (!healing.Empty)
            _damageable.TryChangeDamage(ent.Owner, healing, interruptsDoAfters: false);
    }

    private DamageSpecifier GetHealing(Entity<VirusRadiophasiaComponent> ent, DamageSpecifier coefficients, double exposure)
    {
        var comp = ent.Comp;
        var curTime = _timing.CurTime;
        if (curTime >= comp.NextHealingReset)
        {
            var intervals = (curTime - comp.NextHealingReset).Ticks / HealingInterval.Ticks + 1;
            comp.NextHealingReset += HealingInterval * intervals;
            comp.HealingDemand.Clear();
        }

        var healing = new DamageSpecifier();
        foreach (var (type, coefficient) in coefficients.DamageDict)
        {
            if (coefficient >= 0 || !comp.MaxHealingPerSecond.DamageDict.TryGetValue(type, out var limit) || limit <= 0)
                continue;

            comp.HealingDemand.TryGetValue(type, out var previousDemand);
            var demand = previousDemand - coefficient.Double() * exposure;
            comp.HealingDemand[type] = demand;

            // H(x) = limit * x / (limit + x). Apply only the increment so splitting
            // exposure between events or combining ambient and direct damage cannot bypass the limit.
            var maximum = limit.Double();
            var previousHealing = FixedPoint2.New(maximum * previousDemand / (maximum + previousDemand));
            var totalHealing = FixedPoint2.New(maximum * demand / (maximum + demand));
            if (totalHealing > previousHealing)
                healing.DamageDict[type] = previousHealing - totalHealing;
        }

        return healing;
    }
}
