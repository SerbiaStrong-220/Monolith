using System.Numerics;
using Content.Shared._Exodus.Genetics;
using Content.Shared._Exodus.Virology.Behaviors;
using Content.Shared.Actions;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Spider;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private void InitializeAdaptations()
    {
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticNightVisionEvent>(OnNightVision);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticGlowEvent>(OnGlow);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticHearingEvent>(OnHearing);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticWebEvent>(OnWeb);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticFireBreathEvent>(OnFireBreath);
    }

    private void ReconcileAdaptations(Entity<GeneticEffectsComponent> ent, GeneticAbilityStateComponent state)
    {
        UpdateGeneticSize((ent.Owner, state), ent.Comp.Modifiers.SizeMultiplier);
        UpdateDeflection((ent.Owner, state));
        if (!HasAbility(ent, GeneticAbility.NightVision))
            ent.Comp.NightVisionEnabled = false;
        if (!HasAbility(ent, GeneticAbility.Hearing))
            StopHearing(ent);
        if (!HasAbility(ent, GeneticAbility.Glow))
            StopGlow((ent.Owner, state));
        if (!HasAbility(ent, GeneticAbility.Web))
            RemoveWebs(state);
        Dirty(ent);
    }

    private void CleanupAdaptations(Entity<GeneticAbilityStateComponent> ent)
    {
        UpdateGeneticSize(ent, 1f);
        RemoveDeflector(ent);
        StopGlow(ent);
        RemoveWebs(ent.Comp);
        if (!TerminatingOrDeleted(ent) && TryComp<GeneticEffectsComponent>(ent, out var effects) && !effects.Reverting)
        {
            effects.NightVisionEnabled = false;
            effects.HearingEnabled = false;
            Dirty(ent, effects);
        }
    }

    private void OnNightVision(Entity<GeneticEffectsComponent> ent, ref GeneticNightVisionEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.NightVision))
            return;
        ent.Comp.NightVisionEnabled = !ent.Comp.NightVisionEnabled;
        _actions.SetToggled(args.Action.Owner, ent.Comp.NightVisionEnabled);
        Dirty(ent);
        args.Handled = true;
    }

    private void OnGlow(Entity<GeneticEffectsComponent> ent, ref GeneticGlowEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.Glow))
            return;
        var state = EnsureComp<GeneticAbilityStateComponent>(ent);
        if (state.Glow is { } light && !TerminatingOrDeleted(light))
            StopGlow((ent.Owner, state));
        else
            state.Glow = Spawn(state.GlowPrototype, new EntityCoordinates(ent, Vector2.Zero));
        _actions.SetToggled(args.Action.Owner, state.Glow != null);
        args.Handled = true;
    }

    private void StopGlow(Entity<GeneticAbilityStateComponent> ent)
    {
        if (ent.Comp.Glow is { } light && !TerminatingOrDeleted(light))
            QueueDel(light);
        ent.Comp.Glow = null;
    }

    private void OnHearing(Entity<GeneticEffectsComponent> ent, ref GeneticHearingEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.Hearing))
            return;
        var state = EnsureComp<GeneticAbilityStateComponent>(ent);
        if (!Ready(ent, state.HearingAvailable))
            return;
        state.HearingUntil = _timing.CurTime + state.HearingDuration;
        state.HearingAvailable = _timing.CurTime + state.HearingCooldown;
        ent.Comp.HearingEnabled = true;
        Dirty(ent);
        args.Handled = true;
    }

    private void StopHearing(EntityUid uid)
    {
        if (TryComp<GeneticEffectsComponent>(uid, out var effects) && effects.HearingEnabled)
        {
            effects.HearingEnabled = false;
            Dirty(uid, effects);
        }
    }

    private void OnWeb(Entity<GeneticEffectsComponent> ent, ref GeneticWebEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.Web) || _containers.IsEntityOrParentInContainer(ent) ||
            !_turf.TryGetTileRef(Transform(ent).Coordinates, out var tile) || tile.Value.Tile.IsEmpty)
            return;
        var state = EnsureComp<GeneticAbilityStateComponent>(ent);
        if (!Ready(ent, state.WebAvailable))
            return;
        if (!TryComp<MapGridComponent>(tile.Value.GridUid, out var grid))
            return;
        foreach (var existing in _maps.GetAnchoredEntities(tile.Value.GridUid, grid, tile.Value.GridIndices))
        {
            if (!HasComp<SpiderWebObjectComponent>(existing))
                continue;
            _popup.PopupEntity(Loc.GetString("genetics-web-occupied"), ent, ent);
            return;
        }
        for (var i = state.Webs.Count - 1; i >= 0; i--)
        {
            if (TerminatingOrDeleted(state.Webs[i]))
                state.Webs.RemoveAt(i);
        }
        if (state.Webs.Count >= state.WebLimit)
        {
            _popup.PopupEntity(Loc.GetString("genetics-web-limit"), ent, ent);
            return;
        }
        if (!CanPayNutrition(ent, state.WebNutrition))
            return;
        Reveal(ent);
        state.Webs.Add(Spawn(state.WebPrototype, _turf.GetTileCenter(tile.Value)));
        _hunger.ModifyHunger(ent, -state.WebNutrition);
        state.WebAvailable = _timing.CurTime + state.WebCooldown;
        args.Handled = true;
    }

    private void RemoveWebs(GeneticAbilityStateComponent state)
    {
        foreach (var web in state.Webs)
        {
            if (!TerminatingOrDeleted(web))
                QueueDel(web);
        }
        state.Webs.Clear();
    }

    private void OnFireBreath(Entity<GeneticEffectsComponent> ent, ref GeneticFireBreathEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.FireBreath) ||
            !args.Target.IsValid(EntityManager) || _containers.IsEntityOrParentInContainer(ent))
            return;
        var state = EnsureComp<GeneticAbilityStateComponent>(ent);
        if (!Ready(ent, state.FlameAvailable))
            return;
        if (_inventory.TryGetSlotEntity(ent, "head", out var head) && HasComp<FireBreathSealComponent>(head))
        {
            _popup.PopupEntity(Loc.GetString("genetics-fire-sealed"), ent, ent);
            return;
        }

        var origin = _transform.GetMapCoordinates(ent);
        var target = _transform.ToMapCoordinates(args.Target);
        var direction = target.Position - origin.Position;
        if (origin.MapId != target.MapId || direction.LengthSquared() < 0.01f ||
            direction.LengthSquared() > state.FlameRange * state.FlameRange ||
            !_interaction.InRangeUnobstructed(ent, args.Target, range: state.FlameRange) ||
            !CanPayNutrition(ent, state.FlameNutrition))
            return;

        Reveal(ent);
        // Start at the carrier, so a muzzle offset cannot bypass a wall or closed door.
        var flame = Spawn(state.FlamePrototype, origin);
        _gun.ShootProjectile(flame, Vector2.Normalize(direction), Vector2.Zero, ent, ent, state.FlameSpeed);
        _audio.PlayPvs(state.FlameSound, ent);
        _hunger.ModifyHunger(ent, -state.FlameNutrition);
        state.FlameAvailable = _timing.CurTime + state.FlameCooldown;
        args.Handled = true;
    }

    private bool Ready(EntityUid uid, TimeSpan available)
    {
        if (available <= _timing.CurTime)
            return true;
        _popup.PopupEntity(Loc.GetString("genetics-ability-recovering"), uid, uid);
        return false;
    }

    private bool CanPayNutrition(EntityUid uid, float cost)
    {
        if (cost <= 0 || TryComp<HungerComponent>(uid, out var hunger) &&
            !_hunger.IsHungerBelowState(uid, HungerThreshold.Peckish, _hunger.GetHunger(hunger) - cost, hunger))
            return true;
        _popup.PopupEntity(Loc.GetString("genetics-ability-hungry"), uid, uid);
        return false;
    }
}
