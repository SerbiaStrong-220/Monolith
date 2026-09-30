using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;

namespace Content.Shared._Exodus.Body;

/// <summary>
/// Innate internals toggle for bodies that cannot reach their equipment actions, e.g. a creature running on all fours.
/// It follows the ordinary rules: a worn breath tool and a gas tank in an equipment slot are still required.
/// </summary>
public sealed partial class InternalsActionSystem : EntitySystem
{
    [Dependency] private SharedInternalsSystem _internals = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InternalsComponent, ToggleInternalsActionEvent>(OnToggle);
    }

    private void OnToggle(Entity<InternalsComponent> ent, ref ToggleInternalsActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = _internals.ToggleInternals(ent, ent, false, ent.Comp);
    }
}

public sealed partial class ToggleInternalsActionEvent : InstantActionEvent;
