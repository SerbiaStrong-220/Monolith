using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.ShipShields;

namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// Handles independently collapsible and recoverable layers for ship shield emitters.
/// </summary>
public sealed class LayeredShipShieldSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LayeredShipShieldComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<LayeredShipShieldComponent, ShipShieldOverloadAttemptEvent>(
            OnOverloadAttempt,
            after: new[] { typeof(CdmShieldReserveSystem) });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var delta = TimeSpan.FromSeconds(frameTime);
        var query = EntityQueryEnumerator<LayeredShipShieldComponent,
            ShipShieldEmitterComponent,
            ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var layered, out var emitter, out var power))
        {
            var maximumLayers = GetMaximumLayerCount(layered);
            if (maximumLayers <= 1)
                continue;

            ClampActiveLayerCount(layered, maximumLayers);

            if (layered.ActiveLayerCount >= maximumLayers ||
                emitter.Shield is null ||
                emitter.Recharging ||
                !power.Powered)
            {
                layered.RecoveryAccumulator = TimeSpan.Zero;
                continue;
            }

            var recoveryThreshold = Math.Clamp(layered.RecoveryDamageThreshold, 0f, 1f);
            if (emitter.Damage > emitter.DamageLimit * recoveryThreshold)
            {
                layered.RecoveryAccumulator = TimeSpan.Zero;
                continue;
            }

            if (layered.RecoveryInterval <= TimeSpan.Zero)
            {
                RestoreLayer((uid, layered, emitter), maximumLayers);
                continue;
            }

            layered.RecoveryAccumulator += delta;
            if (layered.RecoveryAccumulator < layered.RecoveryInterval)
                continue;

            layered.RecoveryAccumulator -= layered.RecoveryInterval;
            RestoreLayer((uid, layered, emitter), maximumLayers);
        }
    }

    private void OnStartup(Entity<LayeredShipShieldComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.ActiveLayerCount = GetMaximumLayerCount(ent.Comp);
        ent.Comp.RecoveryAccumulator = TimeSpan.Zero;
    }

    private void OnOverloadAttempt(
        Entity<LayeredShipShieldComponent> ent,
        ref ShipShieldOverloadAttemptEvent args)
    {
        if (args.Cancelled ||
            args.Cause != ShipShieldOverloadCause.Damage ||
            !args.PoweredBeforeLoad ||
            !TryComp<ShipShieldEmitterComponent>(ent, out var emitter) ||
            emitter.Shield is not { } shield ||
            TerminatingOrDeleted(shield))
        {
            return;
        }

        var maximumLayers = GetMaximumLayerCount(ent.Comp);
        if (maximumLayers <= 1)
            return;

        ClampActiveLayerCount(ent.Comp, maximumLayers);
        var overloadDamage = ShipShieldsSystem.CalculateDamageOverloadThreshold(emitter);
        var retainedDamage = Math.Clamp(ent.Comp.CollapseDamageFraction, 0.05f, 0.95f);
        var collapseDamage = ShipShieldsSystem.CalculateSafeDamageAfterOverload(emitter, retainedDamage);
        var ventedDamage = Math.Max(0f, overloadDamage - collapseDamage);
        var collapsed = false;

        while (ent.Comp.ActiveLayerCount > 1 && ShipShieldsSystem.IsDamageOverloaded(emitter))
        {
            ent.Comp.ActiveLayerCount--;
            emitter.Damage = Math.Max(0f, emitter.Damage - ventedDamage);
            collapsed = true;
        }

        if (!collapsed)
            return;

        ent.Comp.RecoveryAccumulator = TimeSpan.Zero;
        UpdateShieldVisuals((ent.Owner, ent.Comp, emitter));

        if (ShipShieldsSystem.IsDamageOverloaded(emitter))
            return;

        emitter.Recharging = false;
        emitter.OverloadAccumulator = 0f;
        emitter.DamageOverloadStartedTick = null;
        args.Cancelled = true;
    }

    private void RestoreLayer(
        Entity<LayeredShipShieldComponent, ShipShieldEmitterComponent> ent,
        int maximumLayers)
    {
        if (ent.Comp1.ActiveLayerCount >= maximumLayers)
            return;

        ent.Comp1.ActiveLayerCount++;
        ent.Comp1.RecoveryAccumulator = TimeSpan.Zero;
        UpdateShieldVisuals(ent);
    }

    private void UpdateShieldVisuals(Entity<LayeredShipShieldComponent, ShipShieldEmitterComponent> ent)
    {
        if (ent.Comp2.Shield is not { } shield ||
            !TryComp<ShipShieldVisualsComponent>(shield, out var visuals))
        {
            return;
        }

        var layerCount = Math.Clamp(ent.Comp1.ActiveLayerCount, 1, GetMaximumLayerCount(ent.Comp1));
        if (visuals.LayerCount == layerCount)
            return;

        visuals.LayerCount = layerCount;
        Dirty(shield, visuals);
    }

    private static void ClampActiveLayerCount(LayeredShipShieldComponent layered, int maximumLayers)
    {
        layered.ActiveLayerCount = Math.Clamp(layered.ActiveLayerCount, 1, maximumLayers);
    }

    private static int GetMaximumLayerCount(LayeredShipShieldComponent layered)
    {
        return Math.Max(1, layered.LayerCount);
    }
}
