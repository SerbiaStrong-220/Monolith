using Content.Shared._Exodus.Mining.AutoMining;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.Mining.AutoMining;

[UsedImplicitly]
public sealed class BulkAutoMiningBoundUserInterface : BoundUserInterface
{
    private BulkAutoMiningWindow? _window;

    public BulkAutoMiningBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<BulkAutoMiningWindow>();
        _window.SetConsole(Owner);
        _window.OnStartPressed += () => SendMessage(new BulkAutoMiningStartMessage());
        _window.OnStopPressed += () => SendMessage(new BulkAutoMiningStopMessage());
        _window.OnTargetRemoved += grid => SendMessage(new BulkAutoMiningSelectGridMessage(grid));
        _window.OnLinkRequested += grid => SendMessage(new BulkAutoMiningLinkRequestMessage(grid));
        _window.OnLinkAccepted += grid => SendMessage(new BulkAutoMiningLinkAcceptMessage(grid));
        _window.OnLinkDeclined += grid => SendMessage(new BulkAutoMiningLinkDeclineMessage(grid));
        _window.OnLinkBroken += grid => SendMessage(new BulkAutoMiningLinkBreakMessage(grid));
        _window.RadarControl.OnGridSelected += grid =>
        {
            if (grid is not { } netGrid)
                return;

            SendMessage(new BulkAutoMiningSelectGridMessage(netGrid));
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not BulkAutoMiningBoundUserInterfaceState castState)
            return;

        _window?.UpdateState(castState);
    }
}
