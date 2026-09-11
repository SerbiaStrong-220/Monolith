// Exodus: шуточные изменения — файловая конфигурация титулов. Для полного отката удалить этот файл и отмеченные точки интеграции.
using System.IO;
using Content.Server._Exodus.Administration;
using Robust.Shared.Player;

namespace Content.Server.Administration.Managers;

public sealed partial class AdminManager
{
    private FixedAdminTitles _fixedAdminTitles = FixedAdminTitles.Disabled;

    private void InitializeFixedAdminTitles()
    {
        // Read the OS file once. Do not use the resource manager: uploaded files must not override it.
        _fixedAdminTitles = FixedAdminTitles.LoadFromFile(Path.Combine(AppContext.BaseDirectory, FixedAdminTitles.FileName), _sawmill);
    }

    public string? GetAdminTitle(ICommonSession session)
    {
        return _fixedAdminTitles.GetTitle(session, GetAdminData(session, includeDeAdmin: true));
    }
}
