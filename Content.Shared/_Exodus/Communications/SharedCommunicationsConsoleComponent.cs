using Content.Shared._Exodus.War;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Communications;

[Virtual]
public partial class SharedCommunicationsConsoleComponent : Component
{
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleInterfaceState : BoundUserInterfaceState
{
    public readonly bool CanAnnounce;
    public readonly bool CanBroadcast = true;
    public List<string>? AlertLevels;
    public string CurrentAlert;
    public float CurrentAlertDelay;
    public readonly WarDeclarationConsoleState? WarState;
    public readonly FactionAlertLevelState? FactionAlertState;
    public readonly string? PendingAlert;
    public readonly TimeSpan PendingAlertAt;
    public readonly Color CurrentAlertColor;

    public CommunicationsConsoleInterfaceState(
        bool canAnnounce,
        List<string>? alertLevels,
        string currentAlert,
        float currentAlertDelay,
        WarDeclarationConsoleState? warState = null,
        FactionAlertLevelState? factionAlertState = null,
        string? pendingAlert = null,
        TimeSpan pendingAlertAt = default,
        Color currentAlertColor = default)
    {
        CanAnnounce = canAnnounce;
        AlertLevels = alertLevels;
        CurrentAlert = currentAlert;
        CurrentAlertDelay = currentAlertDelay;
        WarState = warState;
        FactionAlertState = factionAlertState;
        PendingAlert = pendingAlert;
        PendingAlertAt = pendingAlertAt;
        CurrentAlertColor = currentAlertColor;
    }
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleSelectFactionAlertLevelMessage : BoundUserInterfaceMessage
{
    public readonly string Level;

    public CommunicationsConsoleSelectFactionAlertLevelMessage(string level)
    {
        Level = level;
    }
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleSelectAlertLevelMessage : BoundUserInterfaceMessage
{
    public readonly string Level;

    public CommunicationsConsoleSelectAlertLevelMessage(string level)
    {
        Level = level;
    }
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleAnnounceMessage : BoundUserInterfaceMessage
{
    public readonly string Message;

    public CommunicationsConsoleAnnounceMessage(string message)
    {
        Message = message;
    }
}

[Serializable, NetSerializable]
public sealed class CommunicationsConsoleBroadcastMessage : BoundUserInterfaceMessage
{
    public readonly string Message;
    public CommunicationsConsoleBroadcastMessage(string message)
    {
        Message = message;
    }
}

[Serializable, NetSerializable]
public enum CommunicationsConsoleUiKey
{
    Key
}
