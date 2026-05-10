using Content.Server.EUI;
using Content.Shared._Exodus.Silicons.StationAi;
using Content.Shared.Eui;
using Content.Shared.Preferences;

namespace Content.Server._Exodus.Silicons.StationAi;

public sealed class AiRenameEui : BaseEui
{
    private readonly AiRenameSystem _renameSystem;
    private readonly EntityUid _coreUid;

    public AiRenameEui(AiRenameSystem renameSystem, EntityUid coreUid)
    {
        _renameSystem = renameSystem;
        _coreUid = coreUid;
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
            return;

        _renameSystem.RenameCore(_coreUid, trimmed);
    }
}
