// Exodus validation for editing SS220 chat bans through the common notes window.
using Content.Client.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Administration.Notes;
using Content.Shared.Database;
using Content.Shared.Database._Exodus.Chat;
using Robust.Shared.Utility;

namespace Content.Client.Administration.UI.Notes;

public sealed partial class NoteEdit
{
    [Dependency] private IClientAdminManager _chatBanAdmins = default!;
    private DateTime? _chatBanCreatedAt;

    private void InitializeChatBanEdit(SharedAdminNote? note)
    {
        if (note?.NoteType != NoteType.ChatBan)
            return;
        _chatBanCreatedAt = note.CreatedAt;
        CanEdit &= _chatBanAdmins.HasFlag(AdminFlags.Ban);
    }

    private bool ValidateChatBanEdit()
    {
        if (_chatBanCreatedAt == null)
            return true;

        var reason = Rope.Collapse(NoteTextEdit.TextRope);
        var validReason = !string.IsNullOrWhiteSpace(reason) && reason.Length <= ChatBanLimits.MaxReasonLength;
        var validExpiry = ExpiryTime == null || ExpiryTime > _chatBanCreatedAt &&
            ExpiryTime - _chatBanCreatedAt <= TimeSpan.FromMinutes(ChatBanLimits.MaxDurationMinutes);
        NoteTextEdit.ModulateSelfOverride = validReason ? null : Color.Red;
        NoteTextEdit.ToolTip = validReason ? null : Loc.GetString("chat-ban-invalid-input");
        ExpiryLineEdit.ModulateSelfOverride = validExpiry ? null : Color.Red;
        ExpiryLineEdit.ToolTip = validExpiry ? null : Loc.GetString("chat-ban-invalid-duration");
        return validReason && validExpiry;
    }
}
