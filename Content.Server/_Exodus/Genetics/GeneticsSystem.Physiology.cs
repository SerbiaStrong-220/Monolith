using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticsSystem
{
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly ThirstSystem _thirst = default!;

    private void UpdatePhysiology(EntityUid uid, float seconds)
    {
        if (!TryComp<GeneticEffectsComponent>(uid, out var effects) || effects.Reverting)
            return;

        var modifiers = effects.Modifiers;
        if (modifiers.ClottingRate > 0 && TryComp<BloodstreamComponent>(uid, out var blood) && blood.BleedAmount > 0)
            _bloodstream.TryModifyBleedAmount(uid, -modifiers.ClottingRate * seconds, blood);
        if ((modifiers.NutritionDrain > 0 || modifiers.NutritionMultiplier > 1) && TryComp<HungerComponent>(uid, out var hunger))
        {
            var extra = modifiers.NutritionDrain + hunger.ActualDecayRate * Math.Max(0, modifiers.NutritionMultiplier - 1);
            _hunger.ModifyHunger(uid, -extra * seconds, hunger);
        }
        if (modifiers.ThirstMultiplier > 1 && TryComp<ThirstComponent>(uid, out var thirst) && thirst.UpdateRate > TimeSpan.Zero)
        {
            var extra = thirst.ActualDecayRate * (modifiers.ThirstMultiplier - 1) / (float) thirst.UpdateRate.TotalSeconds;
            _thirst.ModifyThirst(uid, thirst, -extra * seconds);
        }
    }
}
