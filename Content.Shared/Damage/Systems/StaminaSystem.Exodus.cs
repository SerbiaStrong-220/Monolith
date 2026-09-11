// SS220 / Exodus: composable stamina recovery modifiers for virology.
using Content.Shared.Damage.Components;

namespace Content.Shared.Damage.Systems;

public sealed partial class StaminaSystem
{
    public void RefreshStaminaDecay(Entity<StaminaComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        var ev = new RefreshStaminaDecayEvent();
        RaiseLocalEvent(entity, ref ev);
        entity.Comp.DecayModifier = Math.Max(0f, ev.Modifier);
        Dirty(entity);
    }
}

[ByRefEvent]
public record struct RefreshStaminaDecayEvent
{
    public float Modifier = 1f;

    public RefreshStaminaDecayEvent()
    {
    }
}
