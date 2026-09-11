// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Actions;
using Content.Shared._Exodus.Virology.Behaviors;

namespace Content.Server._Exodus.Virology.Behaviors;

public sealed partial class VirusSharpHearingSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private KeenHearingSystem _keen = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VirusSharpHearingComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<VirusSharpHearingComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VirusSharpHearingComponent, GetKeenHearingModifiersEvent>(OnGetKeenModifiers);
    }

    private void OnGetKeenModifiers(Entity<VirusSharpHearingComponent> ent, ref GetKeenHearingModifiersEvent args)
    {
        if (ent.Comp.ForceKeen && !ent.Comp.Reverting)
            args.ForceOn = true;
    }

    private void OnStartup(Entity<VirusSharpHearingComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.ForceKeen)
        {
            if (TryComp<KeenHearingComponent>(ent, out var keen))
                _keen.RefreshKeenHearing((ent.Owner, keen));
        }
        else
        {
            _actions.AddAction(ent.Owner, ref ent.Comp.ActionEntity, ent.Comp.Action);
        }
    }

    private void OnShutdown(Entity<VirusSharpHearingComponent> ent, ref ComponentShutdown args)
    {
        if (Terminating(ent.Owner))
            return;

        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);

        ent.Comp.Reverting = true;
        if (TryComp<KeenHearingComponent>(ent, out var keen))
            _keen.RefreshKeenHearing((ent.Owner, keen));
    }
}
