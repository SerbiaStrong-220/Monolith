// Exodus-begin: confirmed, bounded round-log export owned by the existing authenticated EUI.
using System.Threading.Tasks;
using Content.Server._Exodus.Administration.LogExport;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Robust.Shared.Timing;

namespace Content.Server.Administration.Logs;

public sealed partial class AdminLogsEui
{
    [Dependency] private IGameTiming _exportTiming = default!;

    private AdminLogExportSession? _exportSession;
    private bool _exportClosed;

    private bool CanReadLogs => !_exportClosed && _adminManager.HasAdminFlag(Player, AdminFlags.Logs);

    private bool CanExportRoundLogs => CanReadLogs && _adminManager.HasAdminFlag(Player, AdminFlags.Admin);

    private bool HandleExportMessage(EuiMessageBase message)
    {
        if (!AdminLogExportSession.IsExportMessage(message))
            return false;

        if (_exportClosed)
            return true;

        _exportSession ??= new AdminLogExportSession(Player.UserId, _adminLogs,
            _e.System<AdminLogExportSystem>(), () => _exportTiming.RealTime,
            () => CanExportRoundLogs, () => CurrentRoundId, SendMessage,
            entry =>
            {
                // Keep a server audit trail even when persistence of gameplay admin logs is disabled.
                _sawmill.Info(entry);
                _adminLogs.Add(LogType.AdminCommands, LogImpact.High, $"{entry}");
            });
        _ = HandleExportAsync(message);
        return true;
    }

    private async Task HandleExportAsync(EuiMessageBase message)
    {
        try
        {
            await _exportSession!.HandleMessageAsync(message);
        }
        catch (Exception exception)
        {
            // Do not log exception text; a provider may embed private log contents in it.
            _sawmill.Error($"Admin log export failed with {exception.GetType().Name}");
            _exportSession?.Close();
        }
    }

    private void ExportPermissionsChanged()
    {
        _exportSession?.PermissionsChanged();
        StateDirty();
    }

    private void CloseExport()
    {
        _exportClosed = true;
        _exportSession?.Close();
    }
}
// Exodus-end
