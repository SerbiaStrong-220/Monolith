// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Server.SS220.BackendApi.RequestModels;
using Content.Server.SS220.BackendApi.ResponseModels;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Server.SS220.BackendApi;

public sealed partial class ServerControlController : IPostInjectInit
{
    [Dependency] private readonly IStatusHost _statusHost = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private string? _watchdogToken;
    private string? _watchdogKey;

    private ISawmill _sawmill = default!;

    private static string PlayersCountCommand => "/players";
    private static string ServerStatus => "/server-status";

    private readonly HashSet<string> _postCommands = [PlayersCountCommand];

    private readonly HashSet<string> _getCommands = [ServerStatus];

    public void Initialize()
    {
        _configurationManager.OnValueChanged(CVars.WatchdogToken, _ => UpdateToken());
        _configurationManager.OnValueChanged(CVars.WatchdogKey, _ => UpdateToken());

        UpdateToken();
    }

    public void PostInject()
    {
        _sawmill = Logger.GetSawmill("serverController");
        _statusHost.AddHandler(BackRequestHandler);
    }

    private async Task<bool> BackRequestHandler(IStatusHandlerContext context)
    {
        if ((context.RequestMethod != HttpMethod.Post || !_postCommands.Contains(context.Url.AbsolutePath)) &&
            context.RequestMethod != HttpMethod.Get || !_getCommands.Contains(context.Url.AbsolutePath))
        {
            return false;
        }

        if (!await TryAuth(context))
        {
            return false;
        }

        try
        {
            if (context.Url.AbsolutePath == PlayersCountCommand)
            {
                await PlayersCountHandler(context);
            }
            else if (context.Url.AbsolutePath == ServerStatus)
            {
                await ServerStatusHandler(context);
            }
        }
        catch (Exception exc)
        {
            _sawmill.Error(exc.Message);

            await context.RespondAsync(exc.Message, HttpStatusCode.InternalServerError);
        }

        return true;
    }

    private async Task<bool> TryAuth(IStatusHandlerContext context)
    {
        var auth = context.RequestHeaders["WatchdogToken"];

        if (auth != _watchdogToken)
        {
            _sawmill.Info(@"Failed auth: ""{0}"" vs ""{1}""", auth, _watchdogToken);
            await context.RespondErrorAsync(HttpStatusCode.Unauthorized);
            return false;
        }

        return true;
    }

    private async Task PlayersCountHandler(IStatusHandlerContext context)
    {
        PlayersCountRequestModel request;

        try
        {
            request = await context.RequestBodyJsonAsync<PlayersCountRequestModel>() ?? throw new ArgumentNullException("body", "Parse result is null");
        }
        catch (Exception exc)
        {
            await context.RespondAsync($"Error on comand parameters parse. {Environment.NewLine}{exc.Message}", HttpStatusCode.BadRequest);

            return;
        }

        await context.RespondAsync(_playerManager.PlayerCount.ToString(), HttpStatusCode.OK);
    }

    private async Task ServerStatusHandler(IStatusHandlerContext context)
    {
        var ticker = _entityManager.System<GameTicker>();

        var status = new ServerStatusResponseModel
        {
            PlayersCount = _playerManager.PlayerCount,
            AdminCount = _adminManager.ActiveAdmins.Select(x => _adminManager.GetAdminData(x)).Count(x => x is not null && !x.Stealth),
            RoundDuration = ticker.RoundDuration()
        };

        await context.RespondJsonAsync(status, HttpStatusCode.OK);
    }

    private void UpdateToken()
    {
        var tok = _configurationManager.GetCVar(CVars.WatchdogToken);
        var key = _configurationManager.GetCVar(CVars.WatchdogKey);

        _watchdogToken = string.IsNullOrEmpty(tok) ? null : tok;
        _watchdogKey = string.IsNullOrEmpty(key) ? null : key;
    }
}
