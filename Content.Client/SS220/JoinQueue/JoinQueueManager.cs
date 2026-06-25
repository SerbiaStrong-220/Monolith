// (c) Space Exodus Team - EXDS-RL with CLA

using Content.Shared.SS220.JoinQueue;
using Robust.Client.State;
using Robust.Shared.Network;

namespace Content.Client.SS220.JoinQueue;

public sealed partial class JoinQueueManager
{
    [Dependency] private IClientNetManager _netManager = default!;
    [Dependency] private IStateManager _stateManager = default!;

    public void Initialize()
    {
        _netManager.RegisterNetMessage<MsgQueueUpdate>(OnQueueUpdate);
    }

    private void OnQueueUpdate(MsgQueueUpdate msg)
    {
        if (_stateManager.CurrentState is not QueueState)
        {
            _stateManager.RequestStateChange<QueueState>();
        }

        ((QueueState)_stateManager.CurrentState).OnQueueUpdate(msg);
    }
}
