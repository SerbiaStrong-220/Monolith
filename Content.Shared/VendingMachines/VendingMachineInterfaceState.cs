using Robust.Shared.Serialization;

namespace Content.Shared.VendingMachines
{
    [Serializable, NetSerializable]
    public sealed class VendingMachineEjectMessage : BoundUserInterfaceMessage
    {
        public readonly InventoryType Type;
        public readonly string ID;
        public readonly int? ExpectedPrice; // Exodus: require confirmation of the displayed paid quote.
        public VendingMachineEjectMessage(InventoryType type, string id, int? expectedPrice = null) // Exodus
        {
            Type = type;
            ID = id;
            ExpectedPrice = expectedPrice; // Exodus
        }
    }

    [Serializable, NetSerializable]
    public enum VendingMachineUiKey
    {
        Key,
    }
}
