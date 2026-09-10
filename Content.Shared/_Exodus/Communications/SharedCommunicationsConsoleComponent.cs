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

    public CommunicationsConsoleInterfaceState(
        bool canAnnounce,
        List<string>? alertLevels,
        string currentAlert,
        float currentAlertDelay,
        WarDeclarationConsoleState? warState = null)
    {
        CanAnnounce = canAnnounce;
        AlertLevels = alertLevels;
        CurrentAlert = currentAlert;
        CurrentAlertDelay = currentAlertDelay;
        WarState = warState;
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
