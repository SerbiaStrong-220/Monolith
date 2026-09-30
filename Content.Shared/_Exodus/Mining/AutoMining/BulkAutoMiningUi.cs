using System.Collections.Generic;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Mining.AutoMining;

[Serializable, NetSerializable]
public enum BulkAutoMiningUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class BulkAutoMiningBoundUserInterfaceState : BoundUserInterfaceState
{
    public NavInterfaceState NavState;
    public List<BulkAutoMiningTargetState> SelectedTargets;
    public int MaxSelectableTargets;
    public int ProcessedTiles;
    public int TotalTiles;
    public bool Active;
    public bool CanStart;
    public List<BulkAutoMiningLaserState> LinkedLasers;

    /// <summary>Consortium links of this ship, link requests and nearby ships that can be linked.</summary>
    public BulkMiningLinkUiState Link;

    public BulkAutoMiningBoundUserInterfaceState(
        NavInterfaceState navState,
        List<BulkAutoMiningTargetState> selectedTargets,
        int maxSelectableTargets,
        int processedTiles,
        int totalTiles,
        bool active,
        List<BulkAutoMiningLaserState> linkedLasers,
        bool canStart,
        BulkMiningLinkUiState? link = null)
    {
        NavState = navState;
        SelectedTargets = selectedTargets;
        MaxSelectableTargets = maxSelectableTargets;
        ProcessedTiles = processedTiles;
        TotalTiles = totalTiles;
        Active = active;
        CanStart = canStart;
        LinkedLasers = linkedLasers;
        Link = link ?? new BulkMiningLinkUiState();
    }
}

[Serializable, NetSerializable]
public sealed class BulkAutoMiningSelectGridMessage : BoundUserInterfaceMessage
{
    public NetEntity Grid;

    public BulkAutoMiningSelectGridMessage(NetEntity grid)
    {
        Grid = grid;
    }
}

[Serializable, NetSerializable]
public sealed class BulkAutoMiningStartMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class BulkAutoMiningStopMessage : BoundUserInterfaceMessage;
