using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.MedicalAlerts;

[NetSerializable, Serializable]
public sealed class MedicalAlertListUiState(IReadOnlyList<MedicalAlertEntry> entries, bool notificationsEnabled) : BoundUserInterfaceState
{
    public readonly IReadOnlyList<MedicalAlertEntry> Entries = entries;
    public readonly bool NotificationsEnabled = notificationsEnabled;
}

public enum MedicalAlertCommand : byte
{
    RefreshList = 0,
    ToggleNotifications = 1,
}

[NetSerializable, Serializable]
public sealed class MedicalAlertCommandMessageEvent(MedicalAlertCommand command) : CartridgeMessageEvent
{
    public readonly MedicalAlertCommand Command = command;
}

public abstract class SharedMedicalAlertSystem : EntitySystem
{
    public const int MaxEntries = 64;
}
