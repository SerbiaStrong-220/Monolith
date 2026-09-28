using Content.Client._Exodus.Mining.Pipes.UI;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Lathe;
using Robust.Shared.GameStates;

namespace Content.Client._Exodus.Mining.Pipes;

public sealed partial class MiningRefineryUiSystem : EntitySystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MiningRefineryComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnState(Entity<MiningRefineryComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_ui.TryGetOpenUi<MiningRefineryBoundUserInterface>(ent.Owner, LatheUiKey.Key, out var bui))
            bui.UpdateStorage(ent.Comp);
    }
}
