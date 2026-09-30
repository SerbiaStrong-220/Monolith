using Content.Server.Body.Components;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    [Dependency] private PuddleSystem _puddles = default!;

    private void ReconcileBloodExpulsion(Entity<GeneticAbilityStateComponent> ent)
    {
        var enabled = HasAbility(ent, GeneticAbility.BloodExpulsion);
        if (enabled && !ent.Comp.BloodExpulsionEnabled)
            ent.Comp.NextBloodExpulsion = _timing.CurTime + ent.Comp.BloodExpulsionInterval;
        ent.Comp.BloodExpulsionEnabled = enabled;
    }

    private void UpdateBloodExpulsion(Entity<GeneticAbilityStateComponent> ent)
    {
        if (!ent.Comp.BloodExpulsionEnabled || ent.Comp.NextBloodExpulsion > _timing.CurTime)
            return;

        var interval = ent.Comp.BloodExpulsionInterval;
        if (interval <= TimeSpan.Zero)
            return;

        // Skip missed bursts after a stall instead of draining the carrier repeatedly on consecutive ticks.
        var intervals = (_timing.CurTime - ent.Comp.NextBloodExpulsion).Ticks / interval.Ticks + 1;
        ent.Comp.NextBloodExpulsion += TimeSpan.FromTicks(interval.Ticks * intervals);
        if (!HasAbility(ent, GeneticAbility.BloodExpulsion) ||
            !TryComp<MobStateComponent>(ent, out var mob) || mob.CurrentState == MobState.Dead ||
            !TryComp<BloodstreamComponent>(ent, out var blood))
            return;

        var fraction = Math.Clamp(ent.Comp.BloodExpulsionFraction, 0f, 1f);
        var lostFraction = Math.Min(fraction, _bloodstream.GetBloodLevelPercentage(ent, blood));
        var lost = blood.BloodMaxVolume * lostFraction;
        if (lost <= 0 || !_bloodstream.TryModifyBloodLevel(ent, -lost, blood))
            return;

        if (ent.Comp.BloodExpulsionBleedAmount > 0)
            _bloodstream.TryModifyBleedAmount(ent, ent.Comp.BloodExpulsionBleedAmount, blood);
        _audio.PlayPvs(ent.Comp.BloodExpulsionSound, ent);
        if (ent.Comp.BloodExpulsionRadius <= 0 || ent.Comp.BloodExpulsionSpillAmount <= 0 ||
            _containers.IsEntityOrParentInContainer(ent) ||
            !_turf.TryGetTileRef(Transform(ent).Coordinates, out var origin) ||
            !TryComp<MapGridComponent>(origin.Value.GridUid, out var grid))
            return;

        var radius = ent.Comp.BloodExpulsionRadius;
        var extent = (int) MathF.Ceiling(radius / grid.TileSize);
        var reagent = new ReagentId(blood.BloodReagent, _bloodstream.GetEntityBloodData(ent));
        var amount = ent.Comp.BloodExpulsionSpillAmount * (lostFraction / fraction);
        for (var x = -extent; x <= extent; x++)
        {
            for (var y = -extent; y <= extent; y++)
            {
                var indices = origin.Value.GridIndices + new Vector2i(x, y);
                if (!_maps.TryGetTileRef(origin.Value.GridUid, grid, indices, out var tile) ||
                    tile.Tile.IsEmpty || _turf.IsSpace(tile))
                    continue;

                var coordinates = _maps.GridTileToLocal(origin.Value.GridUid, grid, indices);
                if (!_interaction.InRangeUnobstructed(ent, coordinates, range: radius))
                    continue;

                var solution = new Solution();
                solution.AddReagent(reagent, amount);
                _puddles.TrySpillAt(tile, solution, out _, sound: false);
            }
        }
    }
}
