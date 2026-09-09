// Exodus QoL for the SS220 port: revoke chat bans directly from their history entry.
using Content.Client.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Administration.Notes;
using Content.Shared.Database;
using Robust.Client.Console;

namespace Content.Client.Administration.UI.Notes;

public sealed partial class AdminNotesLinePopup
{
    [Dependency] private IClientAdminManager _chatBanAdmins = default!;
    [Dependency] private IClientConsoleHost _chatBanConsole = default!;

    private void InitializeChatUnban(SharedAdminNote note)
    {
        if (note.NoteType == NoteType.ChatBan && !_chatBanAdmins.HasFlag(AdminFlags.Ban))
            EditButton.Visible = false;

        ChatUnbanButton.Visible = note.NoteType == NoteType.ChatBan && note.UnbannedTime == null &&
            (note.ExpiryTime == null || note.ExpiryTime > DateTime.UtcNow) && _chatBanAdmins.HasFlag(AdminFlags.Ban);
        ChatUnbanButton.OnPressed += _ =>
        {
            _chatBanConsole.ExecuteCommand($"chatunban {note.Id}");
            Close();
        };
    }
}
