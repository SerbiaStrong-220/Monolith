// (c) Space Exodus Team - EXDS-RL with CLA

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.Players;
using Content.Shared.SS220.CCVars;
using Content.Shared.SS220.Discord;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.SS220.Discord;

public sealed class DiscordPlayerManager : IPostInjectInit, IDisposable
{
    internal SponsorUsers? CachedSponsorUsers => _cachedSponsorUsers;

    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IServerNetManager _netMgr = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private ISawmill _sawmill = default!;
    private Timer? _statusRefreshTimer; // We should keep reference or else evil GC will kill our timer
    private volatile SponsorUsers? _cachedSponsorUsers;
    private readonly HttpClient _httpClient = new();

    private string _linkApiUrl = string.Empty;

    private volatile Dictionary<NetUserId, DiscordSponsorInfo?> _cachedSponsorInfo = new();

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("DiscordPlayerManager");

        _netMgr.RegisterNetMessage<MsgUpdatePlayerDiscordStatus>();

        _cfg.OnValueChanged(CCVars220.DiscordLinkApiUrl, v => _linkApiUrl = v, true);
        _cfg.OnValueChanged(CCVars220.DiscordLinkApiKey, v =>
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", v);
        },
        true);

        _statusRefreshTimer = new Timer(async _ =>
            {
                _cachedSponsorUsers = await GetSponsorUsers();
            },
            state: null,
            dueTime: TimeSpan.FromSeconds(_cfg.GetCVar(CCVars220.DiscordSponsorsCacheLoadDelaySeconds)),
            period: TimeSpan.FromSeconds(_cfg.GetCVar(CCVars220.DiscordSponsorsCacheRefreshIntervalSeconds))
        );
    }

    void IPostInjectInit.PostInject()
    {
        _playerManager.PlayerStatusChanged += PlayerManager_PlayerStatusChanged;
    }

    public void Dispose()
    {
        _statusRefreshTimer?.Dispose();
        _httpClient.Dispose();
    }

    private async void PlayerManager_PlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.InGame)
        {
            await UpdateUserDiscordRolesStatus(e);
        }

        if (e.NewStatus == SessionStatus.Disconnected)
        {
            _cachedSponsorInfo.Remove(e.Session.UserId);
        }
    }

    private async Task UpdateUserDiscordRolesStatus(SessionStatusEventArgs e)
    {
        await UpdateSponsorInfo(e.Session.UserId);
        _cachedSponsorInfo.TryGetValue(e.Session.UserId, out var info);

        if (info is not null)
        {
            _netMgr.ServerSendMessage(new MsgUpdatePlayerDiscordStatus
            {
                Info = info
            },
            e.Session.Channel);

            // Cache info in content data
            var contentPlayerData = e.Session.ContentData();
            if (contentPlayerData == null)
                return;

            contentPlayerData.SponsorInfo = info;
        }
    }

    private async Task<DiscordSponsorInfo?> GetSponsorInfo(NetUserId userId)
    {
        if (string.IsNullOrEmpty(_linkApiUrl))
        {
            return null;
        }

        try
        {
            var url = $"{_linkApiUrl}/api/userinfo/{WebUtility.UrlEncode(userId.ToString())}";
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                var errorText = await response.Content.ReadAsStringAsync();

                _sawmill.Error(
                    "Failed to get player sponsor info: [{StatusCode}] {Response}",
                    response.StatusCode,
                    errorText);

                return null;
            }

            return await response.Content.ReadFromJsonAsync<DiscordSponsorInfo>(GetJsonSerializerOptions());
        }
        catch (Exception exc)
        {
            _sawmill.Error(exc.Message);
        }

        return null;
    }

    private static JsonSerializerOptions GetJsonSerializerOptions()
    {
        var opt = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        opt.Converters.Add(new JsonStringEnumConverter());

        return opt;
    }

    public async Task<PrimeListUserStatus?> GetUserPrimeListStatus(Guid userId)
    {
        if (string.IsNullOrEmpty(_linkApiUrl))
        {
            return null;
        }

        try
        {
            var url = $"{_linkApiUrl}/api/checkPrimeAccess/{userId}";

            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                var errorText = await response.Content.ReadAsStringAsync();

                _sawmill.Error(
                    "Failed to get user prime list status: [{StatusCode}] {Response}",
                    response.StatusCode,
                    errorText);

                return null;
            }

            return await response.Content.ReadFromJsonAsync<PrimeListUserStatus>(GetJsonSerializerOptions());
        }
        catch (Exception exc)
        {
            _sawmill.Error(exc.Message);
        }

        return null;
    }

    /// <summary>
    /// Возвращает список спонсоров проекта.
    /// </summary>
    /// <returns></returns>
    internal async Task<SponsorUsers?> GetSponsorUsers()
    {
        if (string.IsNullOrWhiteSpace(_linkApiUrl))
        {
            return null;
        }

        try
        {
            var url = $"{_linkApiUrl}/api/userinfo/sponsors";
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                var errorText = await response.Content.ReadAsStringAsync();

                _sawmill.Error(
                    "Failed to get sponsor users info: [{StatusCode}] {Response}",
                    response.StatusCode,
                    errorText);

                return null;
            }

            return await response.Content.ReadFromJsonAsync<SponsorUsers>(GetJsonSerializerOptions());
        }
        catch (Exception exc)
        {
            _sawmill.Error(exc.Message);
        }

        return null;
    }

    public async Task UpdateSponsorInfo(NetUserId userId)
    {
        var sponsorInfo = await GetSponsorInfo(userId);
        _cachedSponsorInfo[userId] = sponsorInfo;
    }

    public bool TryGetSponsorTierFromCache(NetUserId userId, [NotNullWhen(true)] out DiscordSponsorInfo? info)
    {
        return _cachedSponsorInfo.TryGetValue(userId, out info) && info != null;
    }
}
