using Robust.Shared.GameObjects;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    // Selection can perform several physics raycasts per candidate, so it needs a
    // separate budget from path expansion and closure flood-fill work.
    private const int WorkSelectionBudgetPerTick = 4096;
    private const int PhaseEjectBudgetPerTick = 256;
    private const int QueueIndexBudgetPerTick = 256;
    private int _workSelectionBudget;
    private int _phaseEjectBudget;
    private int _pathOverflowOffset;
    private int _queueBudgetOffset;

    private void ResetWorkSelectionBudget()
    {
        _workSelectionBudget = WorkSelectionBudgetPerTick;
        _phaseEjectBudget = PhaseEjectBudgetPerTick;
    }

    private bool TryConsumeWorkSelectionBudget()
    {
        if (_workSelectionBudget <= 0)
            return false;

        _workSelectionBudget--;
        return true;
    }

    private bool TryConsumePhaseEjectBudget()
    {
        if (_phaseEjectBudget <= 0)
            return false;

        _phaseEjectBudget--;
        return true;
    }

    private int GetQueueIndexBudget(int activeQueues, int queueIndex)
    {
        if (activeQueues <= 0)
            return 0;

        var baseQuota = QueueIndexBudgetPerTick / activeQueues;
        var remainder = QueueIndexBudgetPerTick % activeQueues;
        var rotatedIndex = (queueIndex - _queueBudgetOffset + activeQueues) % activeQueues;
        return baseQuota + (rotatedIndex < remainder ? 1 : 0);
    }

    private void AdvanceQueueIndexBudget(int activeQueues)
    {
        if (activeQueues > 0)
            _queueBudgetOffset = (_queueBudgetOffset + 1) % activeQueues;
    }

    private int GetPathQuota(EntityUid uid, int searches, int pathQuota, bool due)
    {
        if (!due || searches <= 256)
            return pathQuota;

        var slot = (uid.Id + _pathOverflowOffset) % searches;
        return slot < 256 ? 1 : 0;
    }
}
