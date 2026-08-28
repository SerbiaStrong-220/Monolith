using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;

namespace Content.Shared._Exodus.Body;

public sealed class HealthThresholdModifierSystem : EntitySystem
{
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HealthThresholdModifierComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<HealthThresholdModifierComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<HealthThresholdModifierComponent> entity, ref ComponentStartup args)
    {
        if (!_net.IsServer)
            return;

        ApplyModifier(entity.Owner, entity.Comp.Multiplier);
    }

    private void OnShutdown(Entity<HealthThresholdModifierComponent> entity, ref ComponentShutdown args)
    {
        if (!_net.IsServer)
            return;

        if (!float.IsFinite(entity.Comp.Multiplier) || entity.Comp.Multiplier <= 0f)
            return;

        ApplyModifier(entity.Owner, 1f / entity.Comp.Multiplier);
    }

    private void ApplyModifier(EntityUid uid, float multiplier)
    {
        if (!float.IsFinite(multiplier) || multiplier <= 0f || !TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        var originalThresholds = new Dictionary<FixedPoint2, MobState>(thresholds.Thresholds);
        foreach (var (threshold, state) in originalThresholds)
        {
            _mobThreshold.SetMobStateThreshold(uid, threshold * multiplier, state, thresholds);
        }
    }
}
