using Robust.Shared.Serialization;

namespace Content.Shared.Cargo.Events;

/// <summary>
///     Set order in database as approved.
/// </summary>
[Serializable, NetSerializable]
public sealed class CargoConsoleApproveOrderMessage : BoundUserInterfaceMessage
{
    public int OrderId;

    public int? ExpectedPrice; // Exodus: approve only the total shown to the player.

    public CargoConsoleApproveOrderMessage(int orderId, int? expectedPrice = null) // Exodus
    {
        OrderId = orderId;
        ExpectedPrice = expectedPrice; // Exodus
    }
}
