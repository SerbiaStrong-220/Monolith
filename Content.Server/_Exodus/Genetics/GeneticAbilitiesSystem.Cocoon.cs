using Content.Shared._Exodus.Genetics;
using Content.Shared._Exodus.Medical;
using Content.Shared.DoAfter;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    [Dependency] private ThirstSystem _thirst = default!;

    private void InitializeCocoon()
    {
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticCocoonEvent>(OnCocoon);
        SubscribeLocalEvent<GeneticCocoonOwnerComponent, GeneticCocoonDoAfterEvent>(OnCocoonWoven);
        SubscribeLocalEvent<GeneticCocoonOwnerComponent, DoAfterAttemptEvent<GeneticCocoonDoAfterEvent>>(OnCocoonAttempt);
    }

    private void OnCocoon(Entity<GeneticEffectsComponent> ent, ref GeneticCocoonEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.Cocoon) || ent.Comp.InAlternateForm)
            return;

        var owner = EnsureComp<GeneticCocoonOwnerComponent>(ent);
        if (!CanWeaveCocoon((ent.Owner, owner), out _) || !CanPayCocoon((ent.Owner, owner)))
            return;

        Reveal(ent);
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ent, owner.WeaveTime,
            new GeneticCocoonDoAfterEvent(), ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            MultiplyDelay = false,
            CancelDuplicate = false,
            AttemptFrequency = AttemptFrequency.EveryTick,
        });
    }

    private void OnCocoonAttempt(Entity<GeneticCocoonOwnerComponent> ent, ref DoAfterAttemptEvent<GeneticCocoonDoAfterEvent> args)
    {
        if (!CanUse(ent, GeneticAbility.Cocoon) ||
            !TryComp<GeneticEffectsComponent>(ent, out var effects) || effects.InAlternateForm ||
            _containers.IsEntityOrParentInContainer(ent))
            args.Cancel();
    }

    private void OnCocoonWoven(Entity<GeneticCocoonOwnerComponent> ent, ref GeneticCocoonDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !CanUse(ent, GeneticAbility.Cocoon) ||
            !TryComp<GeneticEffectsComponent>(ent, out var effects) || effects.InAlternateForm ||
            !CanWeaveCocoon(ent, out var coordinates) || !CanPayCocoon(ent))
            return;

        Spawn(ent.Comp.Prototype, coordinates);
        _hunger.ModifyHunger(ent, -ent.Comp.NutritionCost);
        if (TryComp<ThirstComponent>(ent, out var thirst))
            _thirst.ModifyThirst(ent, thirst, -ent.Comp.HydrationCost);
        args.Handled = true;
    }

    private bool CanWeaveCocoon(Entity<GeneticCocoonOwnerComponent> ent, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (_containers.IsEntityOrParentInContainer(ent) ||
            !_turf.TryGetTileRef(Transform(ent).Coordinates, out var tile) || tile.Value.Tile.IsEmpty ||
            _turf.IsTileBlocked(tile.Value, CollisionGroup.Impassable) ||
            !TryComp<MapGridComponent>(tile.Value.GridUid, out var grid))
            return false;

        foreach (var anchored in _maps.GetAnchoredEntities(tile.Value.GridUid, grid, tile.Value.GridIndices))
        {
            if (!HasComp<HealingCocoonComponent>(anchored))
                continue;
            _popup.PopupEntity(Loc.GetString("genetics-cocoon-occupied"), ent, ent);
            return false;
        }

        coordinates = _turf.GetTileCenter(tile.Value);
        return true;
    }

    private bool CanPayCocoon(Entity<GeneticCocoonOwnerComponent> ent)
    {
        if (!CanPayNutrition(ent, ent.Comp.NutritionCost))
            return false;
        if (TryComp<ThirstComponent>(ent, out var thirst) &&
            thirst.CurrentThirst - ent.Comp.HydrationCost >= thirst.ThirstThresholds[ThirstThreshold.Parched])
            return true;

        _popup.PopupEntity(Loc.GetString("genetics-cocoon-thirsty"), ent, ent);
        return false;
    }
}
