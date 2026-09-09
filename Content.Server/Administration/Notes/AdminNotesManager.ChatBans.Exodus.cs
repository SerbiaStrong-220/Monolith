// SS220 chat ban history, adapted to refresh open notes panels after moderation actions.
namespace Content.Server.Administration.Notes;

public sealed partial class AdminNotesManager
{
    private async void OnChatBanChanged(int banId)
    {
        try
        {
            if (await _db.GetBanAsNoteAsync(banId) is { } note)
                NoteModified?.Invoke(note.ToShared());
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to update notes for chat ban {banId}: {e}");
        }
    }
}
