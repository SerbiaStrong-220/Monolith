using Robust.Shared.Serialization;

namespace Content.Shared.Crucible;

[RegisterComponent]
public sealed partial class CrucibleRecipeComponent : Component
{
    [DataField("processingTime")]
    public float ProcessingTime = 25f;

    [DataField("resultEntity")]
    public string ResultEntity = "AK570Flatpack";
}

[Serializable, NetSerializable]
public enum CrucibleConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class CrucibleConsoleState : BoundUserInterfaceState
{
    public readonly string ItemName;
    public readonly bool HasItem;
    public readonly bool CanCook;
    public readonly bool IsCooking;
    public readonly float Progress;
    public readonly int RemainingTime;

    public CrucibleConsoleState(
        string itemName,
        bool hasItem,
        bool canCook,
        bool isCooking,
        float progress,
        int remainingTime)
    {
        ItemName = itemName;
        HasItem = hasItem;
        CanCook = canCook;
        IsCooking = isCooking;
        Progress = progress;
        RemainingTime = remainingTime;
    }
}
[Serializable, NetSerializable]
public sealed class CrucibleStartCookMessage : BoundUserInterfaceMessage
{
}
