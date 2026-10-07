// Exodus-begin: permission-checked, bounded round-log download owned by the EUI across pop-out changes.
using Content.Client._Exodus.Administration.LogExport;
using Content.Shared.Eui;
using Robust.Shared.ContentPack;

namespace Content.Client.Administration.UI.Logs;

public sealed partial class AdminLogsEui
{
    [Dependency] private IResourceManager _logExportResources = default!;
    [Dependency] private ILogManager _logExportLogs = default!;
    private AdminLogExportClient _logExport = default!;

    private void InitializeLogExport()
    {
        var sawmill = _logExportLogs.GetSawmill("admin.log-export");
        _logExport = new AdminLogExportClient(
            () => AdminLogExportFile.Create(_logExportResources.UserData),
            SendMessage,
            exception => sawmill.Error($"Local admin-log export failed: {exception}"),
            () => _logExportResources.UserData.OpenOsWindow(AdminLogExportFile.ExportDirectory));
        LogsControl.ExportPanel.Bind(_logExport, () => LogsControl.SelectedRoundId);
    }

    private bool HandleLogExportMessage(EuiMessageBase message)
    {
        if (!AdminLogExportClient.IsExportMessage(message))
            return false;

        _ = _logExport.HandleAsync(message);
        return true;
    }

    private void CloseLogExport()
    {
        LogsControl.ExportPanel.Unbind();
        _ = _logExport.CloseAsync();
    }
}
// Exodus-end
