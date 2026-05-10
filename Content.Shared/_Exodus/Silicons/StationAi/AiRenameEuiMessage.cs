using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Silicons.StationAi;

[Serializable, NetSerializable]
public sealed class AiRenameEuiMessage : EuiMessageBase
{
    public string NewName;

    public AiRenameEuiMessage(string newName)
    {
        NewName = newName;
    }
}

[Serializable, NetSerializable]
public sealed class AiRenameEuiState : EuiStateBase
{
    public string CurrentName;

    public AiRenameEuiState(string currentName)
    {
        CurrentName = currentName;
    }
}
