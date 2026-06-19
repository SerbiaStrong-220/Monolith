// (c) Space Exodus Team - EXDS-RL with CLA

using System.Net.Http;
using System.Net.Http.Headers;
using Content.Shared.SS220.CCVars;
using Content.Shared.SS220.EPA;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server.SS220.EPA;

public sealed partial class EPAManager : IServerEPAManager
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private ILogManager _log = default!;

    private ISawmill _sawmill = default!;
    private readonly HttpClient _http = new();
    private string _apiUrl = string.Empty;

    public void Initialize()
    {
        _sawmill = _log.GetSawmill("epa");

        _config.OnValueChanged(CCVars220.EPAApiUrl, val => _apiUrl = val.TrimEnd('/'), true);
        _config.OnValueChanged(CCVars220.EPAApiKey, val => _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", val), true);

        InitializeAuth();
        InitializeJWT();
    }
}
