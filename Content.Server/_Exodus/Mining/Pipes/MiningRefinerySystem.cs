using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Lathe;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NodeContainer.NodeGroups;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Explosion.Components;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.NodeContainer;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Mining.Pipes;

/// <summary>
/// The normal lathe UI owns recipe selection and looping. This system only handles its exhaust,
/// corrosion, overpressure, and filling its material buffer through mining pipes.
/// </summary>
public sealed class MiningRefinerySystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly NodeContainerSystem _nodes = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly LatheSystem _lathe = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly MiningPipeNetSystem _pipes = default!;
    [Dependency] private readonly ExplosionSystem _explosions = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _materials = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MiningRefineryComponent, LatheStartPrintingEvent>(OnPrinting);
        SubscribeLocalEvent<MiningRefineryComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MiningRefineryComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MiningRefineryComponent, BoundUIOpenedEvent>(OnUiOpen);
        SubscribeLocalEvent<MiningRefineryComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnUiOpen(Entity<MiningRefineryComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (TryComp<MiningPipeNetworkMemberComponent>(ent, out var member))
            _pipes.UpdateClientMaterials((ent, member));

        UpdateStorageState(ent);
    }

    private void OnShutdown(Entity<MiningRefineryComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Exhaust.TotalMoles <= 0 || !TryComp(ent, out TransformComponent? xform) ||
            xform.MapUid is not { } map || TerminatingOrDeleted(map))
            return;

        if (_atmos.GetContainingMixture(ent.Owner, true, true) is { } environment)
            _atmos.Merge(environment, ent.Comp.Exhaust.RemoveRatio(1));
    }

    private void OnMapInit(Entity<MiningRefineryComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _timing.CurTime + UpdateInterval;
    }

    private void OnPrinting(Entity<MiningRefineryComponent> ent, ref LatheStartPrintingEvent args)
    {
        ent.Comp.Exhaust.AdjustMoles(ent.Comp.ExhaustGas, Math.Max(0, ent.Comp.ExhaustMolesPerBatch));
        // Production can cross the limit between scheduled exhaust updates.
        if (!TryDetonate(ent))
            UpdateStorageState(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MiningRefineryComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextUpdate)
                continue;

            comp.NextUpdate += UpdateInterval;
            // Intake runs even with the UI closed and production idle.
            if (TryComp<MiningPipeNetworkMemberComponent>(uid, out var intake))
                _pipes.FillBuffer((uid, intake), comp.SlurryMaterial);

            UpdateExhaust((uid, comp));
            // Remote buffers do not emit MaterialAmountChangedEvent on this lathe.
            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) || !_ui.IsUiOpen(uid, LatheUiKey.Key))
                continue;

            if (TryComp<LatheComponent>(uid, out var lathe) &&
                TryComp<MiningPipeNetworkMemberComponent>(uid, out var member) && _pipes.UpdateClientMaterials((uid, member)))
                _lathe.UpdateUserInterfaceState(uid, lathe);

            UpdateStorageState((uid, comp));
        }
    }

    public void UpdateStorageState(Entity<MiningRefineryComponent> ent)
    {
        var capacity = TryComp<MiningPipeNetworkMemberComponent>(ent, out var member)
            ? _pipes.GetStorageCapacity((ent, member))
            : CompOrNull<MaterialStorageComponent>(ent)?.StorageLimit;
        var state = new MiningRefineryStorageState(ent.Comp.Exhaust.TotalMoles, ent.Comp.Exhaust.Pressure,
            _materials.GetMaterialAmount(ent, ent.Comp.SlurryMaterial), capacity);
        if (state == ent.Comp.StorageState)
            return;

        ent.Comp.StorageState = state;
        Dirty(ent);
    }

    private void UpdateExhaust(Entity<MiningRefineryComponent> ent)
    {
        var exhaust = ent.Comp.Exhaust;
        if (Transform(ent).Anchored &&
            _nodes.TryGetNode(ent.Owner, ent.Comp.ExhaustNode, out PipeNode? outlet) &&
            outlet.NodeGroup is BaseNodeGroup { Removed: false, Remaking: false } &&
            !outlet.Air.Immutable && outlet.Air.Temperature > 0 && exhaust.Temperature > 0 &&
            exhaust.Pressure > outlet.Air.Pressure)
        {
            // Only transfer the amount that equalizes pressure; a blocked outlet cannot delete exhaust.
            var equalizingMoles = (exhaust.Pressure - outlet.Air.Pressure) /
                                 (Content.Shared.Atmos.Atmospherics.R * exhaust.Temperature *
                                  (1 / exhaust.Volume + 1 / outlet.Air.Volume));
            var amount = Math.Min(Math.Max(0, ent.Comp.ExhaustMolesPerSecond), equalizingMoles);
            if (amount > 0)
                _atmos.Merge(outlet.Air, exhaust.Remove(amount));
        }

        if (TryDetonate(ent))
            return;

        if (exhaust.TotalMoles <= ent.Comp.CorrosionThreshold)
        {
            ent.Comp.CorrosionTime = TimeSpan.Zero;
            return;
        }

        ent.Comp.CorrosionTime += UpdateInterval;
        if (ent.Comp.CorrosionTime >= ent.Comp.CorrosionDelay)
            _damage.TryChangeDamage(ent, ent.Comp.CorrosionDamage, ignoreResistances: true);
    }

    private bool TryDetonate(Entity<MiningRefineryComponent> ent)
    {
        if (ent.Comp.ExplosionThreshold <= 0 || ent.Comp.Exhaust.TotalMoles < ent.Comp.ExplosionThreshold ||
            !TryComp<ExplosiveComponent>(ent, out var explosive))
            return false;

        _explosions.TriggerExplosive(ent, explosive);
        return explosive.Exploded;
    }

    private void OnExamined(Entity<MiningRefineryComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("bulk-mining-refinery-exhaust",
            ("pressure", Math.Round(ent.Comp.Exhaust.Pressure)),
            ("moles", Math.Round(ent.Comp.Exhaust.TotalMoles, 1))));

        if (ent.Comp.Exhaust.TotalMoles > ent.Comp.CorrosionThreshold)
            args.PushMarkup(Loc.GetString("bulk-mining-refinery-exhaust-warning"));

        if (ent.Comp.ExplosionThreshold > 0)
            args.PushMarkup(Loc.GetString("bulk-mining-refinery-explosion-limit", ("moles", ent.Comp.ExplosionThreshold)));
    }
}
