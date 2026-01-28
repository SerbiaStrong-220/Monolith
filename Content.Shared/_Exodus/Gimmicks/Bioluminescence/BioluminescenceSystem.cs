using Content.Shared.Actions;
using Content.Shared.Humanoid;

namespace Content.Shared.Exodus.Gimmicks.Bioluminescence;

public sealed partial class BioluminescenceSystem : EntitySystem
{
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BioluminescenceComponent, MapInitEvent>(Startup);
        SubscribeLocalEvent<BioluminescenceComponent, ToggleBioluminescenceEvent>(ToggleBioluminescence);
        SubscribeLocalEvent<BioluminescenceComponent, ComponentShutdown>(Shutdown);
    }

    private void Startup(Entity<BioluminescenceComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<HumanoidAppearanceComponent>(ent, out var humanoid))
            return;

        SharedPointLightComponent? light = null;
        if (!_light.ResolveLight(ent, ref light))
            return;

        // TODO: Add setting to character editor for specifying their bioluminescence color
        var luma = 0.2126 * humanoid.EyeColor.R + 0.7152 * humanoid.EyeColor.G + 0.0722 * humanoid.EyeColor.B;
        if (luma > 75)
            ent.Comp.Color = humanoid.EyeColor;

        _light.SetColor(ent, ent.Comp.Color, light);

        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.ToggleActionPrototype);
        Dirty(ent);
    }

    private void Shutdown(Entity<BioluminescenceComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ActionEntity != null && !ent.Comp.ActionEntity.Value.IsValid())
            return;

        _actions.RemoveAction(ent.Comp.ActionEntity);
    }

    private void ToggleBioluminescence(Entity<BioluminescenceComponent> ent, ref ToggleBioluminescenceEvent args)
    {
        SharedPointLightComponent? light = null;
        if (!_light.ResolveLight(ent, ref light))
            return;

        _actions.SetToggled(ent.Comp.ActionEntity, !light.Enabled);
        _light.SetEnabled(ent, !light.Enabled, light);
    }
}

public sealed partial class ToggleBioluminescenceEvent : InstantActionEvent
{

}
