// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server.RoundEnd;
using Content.Shared._Exodus.CCVar;
using JetBrains.Annotations;
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

    private string? _webapiToken;
    private ISawmill _sawmill = default!;
    private RoundEndSystem? _roundEnd;

    public void Initialize()
    {
        _config.OnValueChanged(CVars.WatchdogToken, _ => UpdateToken());

        UpdateToken();
    }

    private void UpdateToken()
    {
        var tok = _config.GetCVar(XCVars.WebAPIToken);
        _webapiToken = string.IsNullOrEmpty(tok) ? null : tok;
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = Logger.GetSawmill("exds.webapi");

        _statusHost.AddHandler(RequestRoundEnd);
    }

    private async Task<bool> RequestRoundEnd(IStatusHandlerContext context)
    {
        if (context.RequestMethod != HttpMethod.Post || context.Url.AbsolutePath != "/webapi/endround")
        {
            return false;
        }

        if (_webapiToken == null)
        {
            _sawmill.Warning("Watchdog token is unset but received POST /endround API call. Ignoring");
            return false;
        }

        var auth = context.RequestHeaders["WatchdogToken"];

        if (auth != _webapiToken)
        {
            // Holy shit nobody read these logs please.
            _sawmill.Verbose(@"Failed auth: ""{0}"" vs ""{1}""", auth, _webapiToken);
            await context.RespondErrorAsync(HttpStatusCode.Unauthorized);
            return true;
        }

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
                return true;
            }
        }

        parameters ??= new RequestRoundEndParams();

        _roundEnd ??= _entity.System<RoundEndSystem>();

        _task.RunOnMainThread(() =>
        {
            _roundEnd.RequestRoundEnd(parameters.CountdownTime, null, false, parameters.Text, parameters.Name);
        });

        await context.RespondAsync("Success", HttpStatusCode.OK);

        return true;
    }

    [UsedImplicitly]
    public sealed class RequestRoundEndParams
    {
        public TimeSpan CountdownTime { get; set; } = TimeSpan.FromMinutes(10);
        public string Text { get; set; } = "round-end-system-shuttle-called-announcement";
        public string Name { get; set; } = "round-end-system-shuttle-sender-announcement";
    }
}
