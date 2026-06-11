using Content.Server.EUI;
using Content.Shared._Exodus.Silicons.StationAi;
using Content.Shared.Eui;
using Content.Shared.Preferences;

namespace Content.Server._Exodus.Silicons.StationAi;

public sealed class AiRenameEui : BaseEui
{
    private readonly AiRenameSystem _renameSystem;
    private readonly EntityUid _heldUid;
    private readonly string _currentName;

    public AiRenameEui(AiRenameSystem renameSystem, EntityUid heldUid, string currentName)
    {
        _renameSystem = renameSystem;
        _heldUid = heldUid;
        _currentName = currentName;
    }

    public override EuiStateBase GetNewState()
    {
        return new AiRenameEuiState(_currentName);
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        _renameSystem.NotifyEuiClosed(_heldUid, this);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not AiRenameEuiMessage rename)
            return;

        Close();

        if (string.IsNullOrWhiteSpace(rename.NewName))
            return;

        var trimmed = rename.NewName.Trim();
        if (trimmed.Length > HumanoidCharacterProfile.MaxNameLength)
            trimmed = trimmed[..HumanoidCharacterProfile.MaxNameLength];

        _renameSystem.RenameCore(_heldUid, trimmed, Player);
    }
}
