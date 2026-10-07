using Content.Server.Atmos.EntitySystems;
using Content.Server._Exodus.Construction;
using Content.Server.Construction;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Lathe;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NodeContainer.NodeGroups;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Explosion.Components;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.NodeContainer;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Mining.Pipes;

/// <summary>
/// The normal lathe UI owns recipe selection and looping. This system only handles its exhaust,
/// corrosion, overpressure, and filling its material buffer through mining pipes.
/// </summary>
public sealed partial class MiningRefinerySystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private NodeContainerSystem _nodes = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private LatheSystem _lathe = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private MiningPipeNetSystem _pipes = default!;
    [Dependency] private ExplosionSystem _explosions = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MachinePartUpgradeSystem _partUpgrades = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private EntityQuery<MiningPipeNetworkMemberComponent> _memberQuery;
    private EntityQuery<LatheComponent> _latheQuery;

    private float _linkBonus;
    private float _linkBonusDecay;
    private float _linkMaxBonus;

    public override void Initialize()
    {
        base.Initialize();
        _memberQuery = GetEntityQuery<MiningPipeNetworkMemberComponent>();
        _latheQuery = GetEntityQuery<LatheComponent>();
        SubscribeLocalEvent<MiningRefineryComponent, LatheStartPrintingEvent>(OnPrinting);
        SubscribeLocalEvent<MiningRefineryComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MiningRefineryComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MiningRefineryComponent, BoundUIOpenedEvent>(OnUiOpen);
        SubscribeLocalEvent<MiningRefineryComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MiningRefineryComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<MiningRefineryComponent, UpgradeExamineEvent>(OnUpgradeExamine);
        InitializeEfficiency();

        Subs.CVar(_cfg, EXCVars.BulkMiningLinkBonus, value => _linkBonus = value, true);
        Subs.CVar(_cfg, EXCVars.BulkMiningLinkBonusDecay, value => _linkBonusDecay = value, true);
        Subs.CVar(_cfg, EXCVars.BulkMiningLinkMaxBonus, value => _linkMaxBonus = value, true);
    }

    private void OnRefreshParts(Entity<MiningRefineryComponent> ent, ref RefreshPartsEvent args)
    {
        var comp = ent.Comp;
        comp.CapacityMultiplier = Math.Max(1f,
            _partUpgrades.GetMultiplier(args.Parts, comp.MachinePartCapacity, comp.CapacityMultipliers, useRatingAsFallback: true));
        comp.ExhaustMultiplier = _partUpgrades.GetMultiplier(args.Parts, comp.MachinePartExhaust, comp.ExhaustMultipliers);

        // Keep serialized baselines: neither repeated refreshes nor map loading may compound upgrades.
        comp.BaseExhaustVolume ??= comp.Exhaust.Volume;
        comp.BaseCorrosionThreshold ??= comp.CorrosionThreshold;
        comp.BaseExplosionThreshold ??= comp.ExplosionThreshold;
        comp.Exhaust.Volume = comp.BaseExhaustVolume.Value * comp.CapacityMultiplier;
        comp.CorrosionThreshold = comp.BaseCorrosionThreshold.Value * comp.CapacityMultiplier;
        comp.ExplosionThreshold = comp.BaseExplosionThreshold.Value * comp.CapacityMultiplier;

        if (TryComp<MaterialStorageComponent>(ent, out var storage))
        {
            comp.BaseSlurryCapacity ??= storage.StorageLimit;
            if (comp.BaseSlurryCapacity is { } capacity)
            {
                var limit = (int)Math.Clamp((double)capacity * comp.CapacityMultiplier, 0, int.MaxValue);
                _materials.SetStorageLimit((ent, storage), limit);
            }
        }

        if (comp.Exhaust.TotalMoles <= comp.CorrosionThreshold)
            comp.CorrosionTime = TimeSpan.Zero;

        Dirty(ent);
        UpdateStorageState(ent);
    }

    private void OnUpgradeExamine(Entity<MiningRefineryComponent> ent, ref UpgradeExamineEvent args)
    {
        args.AddPercentageUpgrade("bulk-mining-refinery-upgrade-capacity", ent.Comp.CapacityMultiplier);
        args.AddPercentageUpgrade("bulk-mining-refinery-upgrade-exhaust", ent.Comp.ExhaustMultiplier);
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
        ent.Comp.LinkBonusAffectsSpeed = false;
        ent.Comp.NextUpdate = _timing.CurTime + UpdateInterval;
        RefreshFilters(ent);
    }

    private void OnPrinting(Entity<MiningRefineryComponent> ent, ref LatheStartPrintingEvent args)
    {
        var moles = Math.Max(0, ent.Comp.ExhaustMolesPerBatch * ent.Comp.ExhaustMultiplier);
        ent.Comp.Exhaust.AdjustMoles(ent.Comp.ExhaustGas, moles);
        ConsumeFilters(ent, moles);
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
            _memberQuery.TryComp(uid, out var member);
            var process = now >= comp.NextUpdate;
            if (process)
            {
                comp.NextUpdate += UpdateInterval;
                // Intake runs even with the UI closed and production idle.
                if (member != null)
                {
                    _pipes.FillBuffer((uid, member), comp.SlurryMaterial);
                    UpdateLinkBonus((uid, comp), _pipes.CountJoinedShips((uid, member)));
                }

                UpdateExhaust((uid, comp));
            }

            // Coalesce network changes after transfers have finished, independently of the processing timer.
            if (!process && member?.ClientMaterialsDirty != true)
                continue;

            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) || !_ui.IsUiOpen(uid, LatheUiKey.Key))
                continue;

            if (member != null && _pipes.UpdateClientMaterials((uid, member)) && _latheQuery.TryComp(uid, out var lathe))
                _lathe.UpdateUserInterfaceState(uid, lathe);

            UpdateStorageState((uid, comp));
        }
    }

    /// <summary>
    /// Applies the consortium material discount of the joined liquid metal networks.
    /// Mining speed is handled by the lasers; refining speed and machine part upgrades stay independent.
    /// </summary>
    public void UpdateLinkBonus(Entity<MiningRefineryComponent> ent, int ships)
    {
        var bonus = BulkMiningLinkBonus.Get(ships, _linkBonus, _linkBonusDecay, _linkMaxBonus);
        if (ent.Comp.LinkedShips == ships && MathHelper.CloseTo(ent.Comp.LinkBonus, bonus))
            return;

        if (!MathHelper.CloseTo(ent.Comp.LinkBonus, bonus) && _latheQuery.TryComp(ent, out var lathe))
        {
            // Divide out the previous bonus, so repeated changes and loading a saved machine never compound.
            var ratio = (1f + ent.Comp.LinkBonus) / (1f + bonus);
            _lathe.MultiplyLatheMultipliers(ent.Owner, materialUse: ratio);
            ent.Comp.LinkBonus = bonus;
            ent.Comp.LinkBonusAffectsSpeed = false;
            _lathe.UpdateUserInterfaceState(ent, lathe);
        }

        ent.Comp.LinkedShips = ships;
        Dirty(ent);
        UpdateStorageState(ent);
    }

    public void UpdateStorageState(Entity<MiningRefineryComponent> ent)
    {
        var storage = CompOrNull<MaterialStorageComponent>(ent);
        var capacity = TryComp<MiningPipeNetworkMemberComponent>(ent, out var member)
            ? _pipes.GetStorageCapacity((ent, member))
            : storage?.StorageLimit;
        var state = new MiningRefineryStorageState(ent.Comp.Exhaust.TotalMoles, ent.Comp.Exhaust.Pressure,
            _materials.GetMaterialAmount(ent, ent.Comp.SlurryMaterial), capacity, ent.Comp.LinkedShips, ent.Comp.LinkBonus,
            ent.Comp.FullnessDiscount, GetFilterDiscount(ent), ent.Comp.ActiveFilters, ent.Comp.InstalledFilters,
            ent.Comp.FilterSlots.Count, storage?.Storage.GetValueOrDefault(ent.Comp.SlurryMaterial) ?? 0, storage?.StorageLimit);
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
