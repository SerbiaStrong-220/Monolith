using Content.Server._Exodus.Nebula;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Shared._Exodus.Materials;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared._Exodus.Nebula;
using Content.Shared.Atmos;
using Content.Shared.Materials;
using Robust.Shared.Containers;

namespace Content.Server._Exodus.Mining.Pipes;

public sealed partial class MiningRefinerySystem
{
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private NebulaGasSiphonSystem _filters = default!;
    [Dependency] private ConstructionSystem _construction = default!;

    private EntityQuery<NebulaGasSiphonFilterComponent> _filterQuery;

    private void InitializeEfficiency()
    {
        _filterQuery = GetEntityQuery<NebulaGasSiphonFilterComponent>();
        SubscribeLocalEvent<MiningRefineryComponent, ComponentStartup>(OnEfficiencyStartup);
        SubscribeLocalEvent<MiningRefineryComponent, MaterialAmountChangedEvent>(OnRefineryMaterialsChanged);
        SubscribeLocalEvent<MiningRefineryComponent, MaterialStorageCapacityChangedEvent>(OnRefineryCapacityChanged);
        SubscribeLocalEvent<MiningRefineryComponent, EntInsertedIntoContainerMessage>(OnFilterInserted);
        SubscribeLocalEvent<MiningRefineryComponent, EntRemovedFromContainerMessage>(OnFilterRemoved);
    }

    private void OnEfficiencyStartup(Entity<MiningRefineryComponent> ent, ref ComponentStartup args)
    {
        // Only saved bonuses need migrating; untouched prototypes must keep their defaults before MapInit.
        if (ent.Comp.LinkBonusAffectsSpeed && ent.Comp.LinkBonus != 0 && _latheQuery.TryComp(ent, out var lathe))
        {
            _lathe.MultiplyLatheMultipliers((ent.Owner, lathe), time: 1f + ent.Comp.LinkBonus);
            ent.Comp.LinkBonusAffectsSpeed = false;
        }

        // Legacy saved machines have upgraded parts but no serialized exhaust multiplier yet.
        if (TryComp<MachineComponent>(ent, out var machine) && machine.PartContainer.ContainedEntities.Count > 0)
        {
            ent.Comp.ExhaustMultiplier = _partUpgrades.GetMultiplier(_construction.GetAllParts(machine),
                ent.Comp.MachinePartExhaust, ent.Comp.ExhaustMultipliers);
        }

        RefreshFilters(ent);
    }

    private void OnRefineryMaterialsChanged(Entity<MiningRefineryComponent> ent, ref MaterialAmountChangedEvent args)
    {
        UpdateEfficiency(ent);
    }

    private void OnRefineryCapacityChanged(Entity<MiningRefineryComponent> ent, ref MaterialStorageCapacityChangedEvent args)
    {
        UpdateEfficiency(ent);
    }

    private void OnFilterInserted(Entity<MiningRefineryComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (ent.Comp.FilterSlots.Contains(args.Container.ID))
            RefreshFilters(ent);
    }

    private void OnFilterRemoved(Entity<MiningRefineryComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!TerminatingOrDeleted(ent) && ent.Comp.FilterSlots.Contains(args.Container.ID))
            RefreshFilters(ent);
    }

    private void RefreshFilters(Entity<MiningRefineryComponent> ent)
    {
        ent.Comp.ActiveFilters = 0;
        ent.Comp.InstalledFilters = 0;
        var oldStates = TryComp<AppearanceComponent>(ent, out var appearanceComp) &&
            _appearance.TryGetData<MiningRefineryFilterAppearance>(ent, MiningRefineryVisuals.Filters, out var appearance, appearanceComp)
            ? appearance.States
            : null;
        Dictionary<string, MiningRefineryFilterState>? newStates = null;
        foreach (var slot in ent.Comp.FilterSlots)
        {
            var state = MiningRefineryFilterState.Empty;
            if (TryGetFilter(ent, slot, out var filter))
            {
                ent.Comp.InstalledFilters++;
                if (filter.Comp.Remaining >= Atmospherics.GasMinMoles)
                {
                    ent.Comp.ActiveFilters++;
                    state = MiningRefineryFilterState.Intact;
                }
                else
                {
                    state = MiningRefineryFilterState.Depleted;
                }
            }

            if (appearanceComp != null &&
                (oldStates == null || !oldStates.TryGetValue(slot, out var oldState) || oldState != state))
            {
                // Allocate a snapshot only when a cartridge is inserted, removed or depleted.
                newStates ??= oldStates == null ? new() : new(oldStates);
                newStates[slot] = state;
            }
        }

        if (newStates != null)
            _appearance.SetData(ent, MiningRefineryVisuals.Filters, new MiningRefineryFilterAppearance(newStates), appearanceComp);

        UpdateEfficiency(ent);
    }

    private bool TryGetFilter(Entity<MiningRefineryComponent> ent, string slot,
        out Entity<NebulaGasSiphonFilterComponent> filter)
    {
        filter = default;
        if (!_containers.TryGetContainer(ent, slot, out var container) || container.ContainedEntities.Count == 0)
            return false;

        var uid = container.ContainedEntities[0];
        if (TerminatingOrDeleted(uid) || !_filterQuery.TryComp(uid, out var comp))
            return false;

        filter = (uid, comp);
        return true;
    }

    private void ConsumeFilters(Entity<MiningRefineryComponent> ent, float gasMoles)
    {
        if (ent.Comp.FilterSlots.Count == 0)
            return;

        foreach (var slot in ent.Comp.FilterSlots)
        {
            if (TryGetFilter(ent, slot, out var filter))
                _filters.ConsumeFilter(filter, gasMoles * filter.Comp.ConsumptionPerMole);
        }

        RefreshFilters(ent);
    }

    private float GetFilterDiscount(Entity<MiningRefineryComponent> ent)
    {
        return Math.Clamp(ent.Comp.ActiveFilters * ent.Comp.DiscountPerFilter, 0f, 0.95f);
    }

    private void UpdateEfficiency(Entity<MiningRefineryComponent> ent)
    {
        if (ent.Comp.MaxFullnessDiscount <= 0 && ent.Comp.FilterSlots.Count == 0)
            return;

        var fullness = 0f;
        if (TryComp<MaterialStorageComponent>(ent, out var storage) && storage.StorageLimit is > 0)
        {
            // Read the local dictionary: network suppliers must not count towards this tank's fullness.
            fullness = Math.Clamp(storage.Storage.GetValueOrDefault(ent.Comp.SlurryMaterial) /
                (float)storage.StorageLimit.Value, 0f, 1f);
        }

        ent.Comp.FullnessDiscount = fullness * Math.Clamp(ent.Comp.MaxFullnessDiscount, 0f, 0.95f);
        var multiplier = (1f - ent.Comp.FullnessDiscount) * (1f - GetFilterDiscount(ent));
        if (!MathHelper.CloseTo(multiplier, ent.Comp.EfficiencyMultiplier) && _latheQuery.TryComp(ent, out var lathe))
        {
            _lathe.MultiplyLatheMultipliers(ent.Owner, materialUse: multiplier / ent.Comp.EfficiencyMultiplier);
            ent.Comp.EfficiencyMultiplier = multiplier;
            _lathe.UpdateUserInterfaceState(ent, lathe);
        }

        UpdateStorageState(ent);
    }
}
