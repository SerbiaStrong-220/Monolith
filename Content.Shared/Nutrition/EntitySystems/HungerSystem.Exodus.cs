using Content.Shared._Exodus.Nutrition;
using Content.Shared.Nutrition.Components;

namespace Content.Shared.Nutrition.EntitySystems;

// Exodus: defer a waiting ghost role's hunger until its first possession.
public sealed partial class HungerSystem
{
    [Dependency] private NeedsActivationSystem _needsActivation = default!;

    private void InitializeNeedsActivation()
    {
        SubscribeLocalEvent<HungerComponent, NeedsActivationChangedEvent>(OnNeedsActivationChanged);
    }

    private void OnNeedsActivationChanged(Entity<HungerComponent> ent, ref NeedsActivationChangedEvent args)
    {
        DoHungerThresholdEffects(ent, ent.Comp, force: true);
        // Replicate both the new decay rate and the hunger snapshot used as its starting point.
        Dirty(ent);
    }
}
