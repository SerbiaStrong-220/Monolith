// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Server.RoundEnd;
using Content.Shared._Exodus.CCVar;
using Robust.Server;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Shared;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;

namespace Content.Server._Exodus.Adminbus.WebAPI;

public sealed partial class WebAPI : IPostInjectInit
{
    [Dependency] private readonly IStatusHost _statusHost = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly ITaskManager _task = default!;
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IBaseServer _server = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IAdminManager _admin = default!;

    private string? _webapiToken;
    private ISawmill _sawmill = default!;
    private RoundEndSystem? _roundEnd;
    private GameTicker? _ticker;

    private delegate Task RouteHandler(IStatusHandlerContext context);

    // {endpoint, (HttpMethod, HandlerFunction, requiresAuth?)}
    private Dictionary<string, (HttpMethod, RouteHandler, bool)> _endpoints = new();

    public void Initialize()
    {
        _config.OnValueChanged(CVars.WatchdogToken, _ => UpdateToken());

        UpdateToken();

        // {endpoint, (HttpMethod, HandlerFunction, requiresAuth?)}
        _endpoints.Add("/webapi/endround", new(HttpMethod.Post, RequestRoundEnd, true));
        _endpoints.Add("/webapi/shutdown", new(HttpMethod.Post, RequestShutdown, true));
        _endpoints.Add("/webapi/status", new(HttpMethod.Get, GetServerStatus, true));
    }

    private void UpdateToken()
    {
        var tok = _config.GetCVar(XCVars.WebAPIToken);
        _webapiToken = string.IsNullOrEmpty(tok) ? null : tok;
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = Logger.GetSawmill("exds.webapi");

        _statusHost.AddHandler(EndpointsHandler);
    }

    private async Task<bool> EndpointsHandler(IStatusHandlerContext context)
    {

        foreach (var (path, (method, handler, requiresAuth)) in _endpoints)
        {
            if (context.Url.AbsolutePath != path || context.RequestMethod != method)
                continue;

            if (requiresAuth)
            {
                if (_webapiToken == null)
                {
                    _sawmill.Warning($"WebAPI token is unset but received {method} {path} API call. Ignoring");
                    return false;
                }

                if (!context.RequestHeaders.TryGetValue("WebAPIToken", out var auth) || auth != _webapiToken)
                {
                    await context.RespondErrorAsync(HttpStatusCode.Unauthorized);
                    return true;
                }
            }

            // TODO: handle request params validation

            if (handler == null)
                return false;

            await handler.Invoke(context);

            return true;
        }

        return false;
    }

    private async Task RequestRoundEnd(IStatusHandlerContext context)
    {
        RequestRoundEndParams? parameters = null;
        if (context.RequestHeaders.TryGetValue("Content-Type", out var contentType)
            && contentType == MediaTypeNames.Application.Json)
        {
            try
            {
                parameters = await context.RequestBodyJsonAsync<RequestRoundEndParams>();
            }
            catch (JsonException)
            {
                // parameters null so it'll catch the block down below.
            }

            if (parameters == null)
            {
                await context.RespondErrorAsync(HttpStatusCode.BadRequest);
                return;
            }
        }

        parameters ??= new RequestRoundEndParams();

        _roundEnd ??= _entity.System<RoundEndSystem>();

        _task.RunOnMainThread(() =>
        {
            _roundEnd.RequestRoundEnd(parameters.CountdownTime, null, false, parameters.Text, parameters.Name);
        });

        await context.RespondAsync("Success", HttpStatusCode.OK);
    }

    private async Task RequestShutdown(IStatusHandlerContext context)
    {
        RequestShutdownParams? parameters = null;
        if (context.RequestHeaders.TryGetValue("Content-Type", out var contentType)
            && contentType == MediaTypeNames.Application.Json)
        {
            try
            {
                parameters = await context.RequestBodyJsonAsync<RequestShutdownParams>();
            }
            catch (JsonException)
            {
                // parameters null so it'll catch the block down below.
            }

            if (parameters == null)
            {
                await context.RespondErrorAsync(HttpStatusCode.BadRequest);
                return;
            }
        }

        parameters ??= new RequestShutdownParams();

        _task.RunOnMainThread(() =>
        {
            _server.Shutdown(parameters.Reason);
        });

        await context.RespondAsync("Success", HttpStatusCode.OK);
    }

    private async Task GetServerStatus(IStatusHandlerContext context)
    {
        _ticker ??= _entity.System<GameTicker>();

        var adminCount = _admin.ActiveAdmins
            .Count(a => _admin.GetAdminData(a) is { Stealth: false });
        var playerCount = _player.PlayerCount;
        var admins = _admin.ActiveAdmins.ToDictionary(a => a.Name, a => _admin.GetAdminData(a)?.Stealth ?? true);

        var response = new StatusResponseDTO()
        {
            AdminCount = adminCount,
            PlayersCount = playerCount,
            RoundDuration = _ticker.RoundDuration(),
            Admins = admins,
        };

        await context.RespondJsonAsync(response, HttpStatusCode.OK);
    }

    public sealed class RequestRoundEndParams
    {
        public TimeSpan CountdownTime { get; set; } = TimeSpan.FromMinutes(10);
        public string Text { get; set; } = "round-end-system-shuttle-called-announcement";
        public string Name { get; set; } = "round-end-system-shuttle-sender-announcement";
    }

    public sealed class RequestShutdownParams
    {
        public string? Reason { get; set; } = null;
    }

    public sealed class StatusResponseDTO
    {
        public int PlayersCount { get; set; }
        public TimeSpan RoundDuration { get; set; }
        public int AdminCount { get; set; }
        // <CKey, InStealth>
        public Dictionary<string, bool> Admins { get; set; } = new();
    }
}
