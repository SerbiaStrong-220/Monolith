// (c) Space Exodus Team - EXDS-RL with CLA

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Content.Server.SS220.EPA.DTO;
using Content.Server.SS220.Extensions;
using Content.Shared.CCVar;
using Content.Shared.SS220.CCVars;
using Content.Shared.SS220.EPA;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server.SS220.EPA;

public sealed partial class EPAManager
{
    private sealed class EPAHandshakeState
    {
        public required TaskCompletionSource Tcs;
        public required INetChannel Channel;
        public string? SessionCode;
    }

    private AuthMode _authMode;
    private EPAMode _epaMode;

    // (PeerConnectionId, TCS)
    private readonly ConcurrentDictionary<long, EPAHandshakeState> _handshakes = new();

    /// <inheritdoc />
    public event Func<INetChannel, Task> AuthFinished
    {
        add => _authFinishedEvent.Add(value);
        remove => _authFinishedEvent.Remove(value);
    }

    private readonly List<Func<INetChannel, Task>> _authFinishedEvent = new();
    private async Task OnAuthFinished(INetChannel channel)
    {
        foreach (var handler in _authFinishedEvent)
        {
            await handler(channel);
        }
    }

    private void InitializeAuth()
    {
        _config.OnValueChanged(CCVars.AuthMode, val => _authMode = Enum.IsDefined((AuthMode)val) ? (AuthMode)val : _authMode, true);
        _config.OnValueChanged(CCVars220.EPAMode, val => _epaMode = Enum.IsDefined((EPAMode)val) ? (EPAMode)val : _epaMode, true);

        _net.InitialHandshakeComplete += OnHandshake;
        _net.Disconnect += OnDisconnect;

        _net.RegisterNetMessage<MsgEPALogin>(OnLogin, accept: NetMessageAccept.Server | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPACheckSession>(OnCheckSession, accept: NetMessageAccept.Server | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPACreateSession>(OnCreateSession, accept: NetMessageAccept.Server | NetMessageAccept.Handshake);

        _net.RegisterNetMessage<MsgEPAHello>(accept: NetMessageAccept.Client | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPAReject>(accept: NetMessageAccept.Client | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPAAccept>(accept: NetMessageAccept.Client | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPACreateSessionRes>(accept: NetMessageAccept.Client | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPANewSession>(accept: NetMessageAccept.Client | NetMessageAccept.Handshake);
    }

    #region Connection Events

    private Task OnHandshake(NetChannelArgs args)
    {
        var msg = new MsgEPAHello
        {
            ShouldAuth = _epaMode != EPAMode.Disabled
        };
        args.Channel.SendMessage(msg);

        if (_epaMode == EPAMode.Disabled)
        {
            return OnAuthFinished(args.Channel);
        }

        _sawmill.Debug($"Paused handshake for {args.Channel.ToPrettyString()} and waiting next steps");

        var tcs = new TaskCompletionSource();

        var state = new EPAHandshakeState
        {
            Channel = args.Channel,
            Tcs = tcs
        };
        _handshakes.TryAdd(args.Channel.ConnectionId, state);

        return tcs.Task;
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        CancelHandshake(args.Channel);
    }

    #endregion

    #region Message Handlers

    private async void OnLogin(MsgEPALogin msg)
    {
        if (_epaMode == EPAMode.Disabled)
            return;

        if (!TryReadToken(msg.MsgChannel, msg.Token, out var payload))
        {
            _sawmill.Debug($"{msg.MsgChannel.ToPrettyString()} token rejected, token was: {msg.Token}");

            SendReject(msg.MsgChannel, "epa-token-invalid-message");

            return;
        }

        var channel = msg.MsgChannel;

        if (_epaMode == EPAMode.Authorization)
        {
            var guid = payload.UserId;
            var userId = new NetUserId(guid);
            var newData = new NetUserData(userId, payload.Username)
            {
                CreatedTime = channel.UserData.CreatedTime,
                HWId = channel.UserData.HWId,
                ModernHWIds = channel.UserData.ModernHWIds,
                // PatronTier
                // Trust
            };
            _sawmill.Debug("Performing net channel re-setup");
            _net.ReSetupChannel(msg.MsgChannel, newData, LoginType.LoggedIn);
        }

        await OnAuthFinished(msg.MsgChannel);

        ReleaseHandshake(channel);

        var acceptMsg = new MsgEPAAccept()
        {
            UserId = channel.UserId,
            Username = channel.UserName,
        };
        channel.SendMessage(acceptMsg);
    }

    private async void OnCreateSession(MsgEPACreateSession msg)
    {
        if (_epaMode == EPAMode.Disabled)
            return;

        // temporal mechanism for soft migration of players to full EPA auth
        if (_epaMode == EPAMode.Validation
            && _authMode == AuthMode.Required
            && msg.MsgChannel.AuthType == LoginType.LoggedIn)
        {
            var token = await RequestTokenAsync(msg.MsgChannel.UserId);
            var newSession = new MsgEPANewSession()
            {
                Token = token,
            };
            msg.MsgChannel.SendMessage(newSession);
            return;
        }

        await BeginSessionCreationAsync(msg.MsgChannel);
    }

    private async void OnCheckSession(MsgEPACheckSession msg)
    {
        if (_epaMode == EPAMode.Disabled)
            return;

        if (TryGetHandshakeState(msg.MsgChannel, out var state))
        {
            if (state.SessionCode == null)
            {
                _sawmill.Warning($"{msg.MsgChannel.ToPrettyString()} asked to validate session without a session code");
                return;
            }

            var (status, token) = await CheckSessionCodeAsync(state.SessionCode);

            switch (status)
            {
                case EPASessionStatus.Waiting:
                    return;

                case EPASessionStatus.Expired:
                    await BeginSessionCreationAsync(msg.MsgChannel);
                    return;

                case EPASessionStatus.Rejected:
                    SendReject(msg.MsgChannel, "epa-session-rejected");
                    return;

                default:
                    break;
            }

            DebugTools.Assert(token != null);

            var res = new MsgEPANewSession()
            {
                Token = token
            };
            msg.MsgChannel.SendMessage(res);
        }
    }

    #endregion

    #region Private API

    private bool TryGetHandshakeState(INetChannel channel, [NotNullWhen(true)] out EPAHandshakeState? state)
    {
        state = null;

        if (_handshakes.TryGetValue(channel.ConnectionId, out var fetched)) // why do TryGetValue returns non-nullable result?
        {
            state = fetched;
            return true;
        }

        return false;
    }

    private void CancelHandshake(INetChannel channel)
    {
        if (TryGetHandshakeState(channel, out var state))
        {
            _sawmill.Debug($"{channel.ToPrettyString()} disconnected during handshake");
            _handshakes.TryRemove(channel.ConnectionId, out _);
            state.Tcs.SetCanceled();
        }
    }

    private void ReleaseHandshake(INetChannel channel)
    {
        if (TryGetHandshakeState(channel, out var state))
        {
            _handshakes.TryRemove(channel.ConnectionId, out _);
            state.Tcs.SetResult();
        }
    }

    private async Task BeginSessionCreationAsync(INetChannel channel)
    {
        if (!TryGetHandshakeState(channel, out var state))
        {
            throw new InvalidOperationException("Tried to create new session for channel after handshake completion");
        }

        var (authUrl, code) = await CreateSessionAsync();
        state.SessionCode = code;

        var res = new MsgEPACreateSessionRes()
        {
            AuthUrl = authUrl,
        };
        channel.SendMessage(res);
    }

    private void SendReject(INetChannel channel, string message)
    {
        var rejectMsg = new MsgEPAReject
        {
            Reason = message
        };

        channel.SendMessage(rejectMsg);
    }

    #region HTTP API

    private async Task<(string AuthUrl, string SessionCode)> CreateSessionAsync()
    {
        var res = await _http.GetAsync($"{_apiUrl}/ss14/GameAuth/getLink");
        var data = await res.Content.ReadFromJsonAsync<CreateSessionResDTO>() ??
            throw new SerializationException("Deserialized object is null");

        return (data.AuthUrl, data.SessionCode);
    }

    private async Task<string> RequestTokenAsync(Guid uuid)
    {
        var res = await _http.PostAsync($"{_apiUrl}/ss14/GameAuth/requestToken/{uuid}", null);
        var data = await res.Content.ReadFromJsonAsync<RequestTokenResDTO>() ??
            throw new SerializationException("Failed to deserialize server response");

        return data.Token;
    }

    private async Task<(EPASessionStatus, string?)> CheckSessionCodeAsync(string code)
    {
        var res = await _http.GetAsync($"{_apiUrl}/ss14/GameAuth/check?key={Uri.EscapeDataString(code)}");
        var data = await res.Content.ReadFromJsonAsync<CheckSessionResDTO>() ??
            throw new SerializationException("Failed to deserialize server response");

        if (data.Status == EPASessionStatus.Passed && data.Token == null)
            throw new Exception("Server sent invalid response");
        else if (data.Status != EPASessionStatus.Passed && data.Token != null)
            throw new Exception("Server sent invalid response");

        return (data.Status, data.Token);
    }

    #endregion HTTP API

    #endregion Private API
}
