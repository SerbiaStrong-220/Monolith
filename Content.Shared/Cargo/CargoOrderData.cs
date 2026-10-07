using Robust.Shared.Serialization;
using Content.Shared.Access.Components;
using System.Text;
namespace Content.Shared.Cargo
{
    [NetSerializable, Serializable]
    public sealed class CargoOrderData
    {
        /// <summary>
        /// Price when the order was added.
        /// </summary>
        public double Price; // Exodus: preserve fractional resale prices until the final transaction total.

        /// <summary>
        /// Exodus: current quote for the entire order, or the amount paid after approval.
        /// Null uses the legacy unit price and quantity.
        /// </summary>
        public int? TotalPrice; // Exodus exact order quote

        /// <summary>
        /// A unique (arbitrary) ID which identifies this order.
        /// </summary>
        public readonly int OrderId;

        /// <summary>
        /// Prototype Id for the item to be created
        /// </summary>
        public readonly string ProductId;

        /// <summary>
        /// Prototype Name
        /// </summary>
        public readonly string ProductName;

        /// <summary>
        /// The number of items in the order. Not readonly, as it might change
        /// due to caps on the amount of orders that can be placed.
        /// </summary>
        public int OrderQuantity;

        /// <summary>
        /// How many instances of this order that we've already dispatched
        /// </summary>
        public int NumDispatched = 0;

        public readonly string Requester;
        // public String RequesterRank; // TODO Figure out how to get Character ID card data
        // public int RequesterId;
        [DataField]
        public string Reason { get; private set; }
        public  bool Approved;
        [DataField]
        public string? Approver;

        public NetEntity? Computer = null;

        /// <summary>
        /// Exodus: order filled from station cargo market stock (sold goods), not YAML catalog.
        /// Stock is deducted on approve; ProductId is the entity prototype to spawn.
        /// </summary>
        public bool FromResaleStock; // Exodus

        public CargoOrderData(int orderId, string productId, string productName, double price, int amount, string requester, string reason, NetEntity? computer, bool fromResaleStock = false) // Exodus: fractional price and resale stock
        {
            OrderId = orderId;
            ProductId = productId;
            ProductName = productName;
            Price = price;
            OrderQuantity = amount;
            Requester = requester;
            Reason = reason;
            Computer = computer;
            FromResaleStock = fromResaleStock; // Exodus
        }

        public void SetApproverData(string? approver)
        {
            Approver = approver;
        }

        public void SetApproverData(string? fullName, string? jobTitle)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                sb.Append($"{fullName} ");
            }
            if (!string.IsNullOrWhiteSpace(jobTitle))
            {
                sb.Append($"({jobTitle})");
            }
            Approver = sb.ToString();
        }
    }
}
