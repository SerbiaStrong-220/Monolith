using Content.Shared.Mining;

namespace Content.Server._Exodus.Mining;

public sealed partial class OreDropModifierSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OreDropModifierComponent, MaxOreYieldModifierEvent>(OnModifierEvent);
    }

    private void OnModifierEvent(Entity<OreDropModifierComponent> ent, ref MaxOreYieldModifierEvent ev)
    {
        ev.Modify(ent.Comp.Modifier);
    }
}
