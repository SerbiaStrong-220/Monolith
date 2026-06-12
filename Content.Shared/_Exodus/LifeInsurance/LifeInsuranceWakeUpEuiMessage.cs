using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.LifeInsurance;

/// <summary>
/// Sent by the client when the life insurance wake-up window is dismissed.
/// </summary>
[Serializable, NetSerializable]
public sealed class LifeInsuranceWakeUpClosedMessage : EuiMessageBase
{
}
