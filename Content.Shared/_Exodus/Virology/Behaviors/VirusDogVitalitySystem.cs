// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Network;

namespace Content.Shared._Exodus.Virology.Behaviors;

public sealed partial class VirusDogVitalitySystem : EntitySystem
{
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VirusDogVitalityComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<VirusDogVitalityComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<VirusDogVitalityComponent> ent, ref ComponentStartup args)
    {
        if (_net.IsClient
            || !_mobThreshold.TryGetThresholdForState(ent, MobState.Critical, out var critical)
            || !_mobThreshold.TryGetThresholdForState(ent, MobState.Dead, out var dead))
            return;

        var newDead = dead.Value + ent.Comp.Threshold + ent.Comp.DeathThresholdOffset;
        var newCritical = FixedPoint2.Min(critical.Value + ent.Comp.Threshold, newDead - FixedPoint2.New(0.01));
        ent.Comp.AppliedCriticalBonus = newCritical - critical.Value;
        ent.Comp.AppliedDeathBonus = newDead - dead.Value;
        _mobThreshold.SetMobStateThreshold(ent, newDead, MobState.Dead);
        _mobThreshold.SetMobStateThreshold(ent, newCritical, MobState.Critical);
    }

    private void OnShutdown(Entity<VirusDogVitalityComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsClient || Terminating(ent))
            return;

        if (_mobThreshold.TryGetThresholdForState(ent, MobState.Critical, out var critical))
            _mobThreshold.SetMobStateThreshold(ent, critical.Value - ent.Comp.AppliedCriticalBonus, MobState.Critical);
        if (_mobThreshold.TryGetThresholdForState(ent, MobState.Dead, out var dead))
            _mobThreshold.SetMobStateThreshold(ent, dead.Value - ent.Comp.AppliedDeathBonus, MobState.Dead);
    }
}
