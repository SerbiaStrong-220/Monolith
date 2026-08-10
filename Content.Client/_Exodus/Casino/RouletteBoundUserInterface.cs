using Content.Shared._Exodus.Casino;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Casino;

public sealed class RouletteBoundUserInterface : BoundUserInterface
{
    [Dependency] private IGameTiming _timing = default!;

    private RouletteWindow? _window;
    private uint _requestId;
    private bool _requestPending;

    public RouletteBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        if (_requestId == 0)
            _requestId = _timing.CurTick.Value;

        _window = this.CreateWindow<RouletteWindow>();
        _window.BetPlaced += PlaceBet;
        if (State is RouletteUiState state)
            _window.UpdateState(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is RouletteUiState rouletteState)
            _window?.UpdateState(rouletteState);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        switch (message)
        {
            case RouletteStateMessage stateMessage:
                _window?.UpdateState(stateMessage.State);
                break;
            case RouletteBetResultMessage result when result.RequestId == _requestId:
                _requestPending = false;
                _window?.SetRequestPending(false);
                if (result.Error != RouletteBetError.None)
                    _window?.ShowError(result.Error);
                break;
        }
    }

    private void PlaceBet(RouletteBet bet, uint roundId)
    {
        if (_requestPending)
            return;

        _requestPending = true;
        _window?.SetRequestPending(true);
        _requestId++;
        SendMessage(new RoulettePlaceBetMessage(bet, roundId, _requestId));
    }
}
