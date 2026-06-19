// (c) Space Exodus Team - EXDS-RL with CLA

using System.Diagnostics.CodeAnalysis;
using System.IO;
using Content.Shared.SS220.EPA;
using Robust.Client.Configuration;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Client.SS220.EPA;

public sealed partial class EPAManager : IClientEPAManager
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IResourceManager _resource = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IClientNetConfigurationManager _netConfig = default!;
    [Dependency] private IStateManager _state = default!;

    public string? AuthUrl { get; private set; }

    private const string DataFilePath = "EPA.dat";
    private const int DataFileSizeLimit = 16 * 1024;
    private static readonly ResPath BaseDataPath = new("/SS220");

    private ISawmill _sawmill = default!;
    private (NetUserId UserId, string Username)? _credentials;

    public void Initialize()
    {
        _sawmill = _log.GetSawmill("epa");

        _net.Connected += OnConnected;

        _net.RegisterNetMessage<MsgEPALogin>(accept: NetMessageAccept.Server | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPACheckSession>(accept: NetMessageAccept.Server | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPACreateSession>(accept: NetMessageAccept.Server | NetMessageAccept.Handshake);

        _net.RegisterNetMessage<MsgEPAHello>(OnHello, accept: NetMessageAccept.Client | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPAReject>(OnReject, accept: NetMessageAccept.Client | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPAAccept>(OnAccept, accept: NetMessageAccept.Client | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPACreateSessionRes>(OnCreateSessionRes, accept: NetMessageAccept.Client | NetMessageAccept.Handshake);
        _net.RegisterNetMessage<MsgEPANewSession>(OnNewSession, accept: NetMessageAccept.Client | NetMessageAccept.Handshake);
    }

    #region Message Handlers

    private void OnHello(MsgEPAHello msg)
    {
        if (msg.ShouldAuth)
            TryAuthorize();
    }

    private void OnReject(MsgEPAReject msg)
    {
        RequestNewSession();
    }

    private void OnAccept(MsgEPAAccept msg)
    {
        _credentials = (new NetUserId(msg.UserId), msg.Username);
    }

    private void OnCreateSessionRes(MsgEPACreateSessionRes msg)
    {
        AuthUrl = msg.AuthUrl;

        if (_state.CurrentState is EPAAuthState)
            return;

        _state.RequestStateChange<EPAAuthState>();
    }

    private void OnNewSession(MsgEPANewSession msg)
    {
        if (TryWriteToken(msg.Token))
        {
            _sawmill.Info("New token acquired and saved");

            _credentials = null;
            TryAuthorize();
        }
    }

    #endregion

    #region Connection Events

    private void OnConnected(object? sender, NetChannelArgs args)
    {
        // Note: It is important for our code to be executed right after SetupMultiplayer call
        // SetupMultiplayer call (from BaseClient) -> MsgPlayerListReq sent -> our call, nothing in between, no Lidgren handlers or any other handlers
        // If something will subscribe to this event it can break our logic just because it will shift our call before SetupMultiplayer
        // If OnReceivedClientData suddenly will start crushing - this is your research point

        // God has abandoned us. Good luck.
        _netConfig.ReceivedInitialNwVars += OnReceivedClientData;
    }

    /// <see cref="OnConnected"/> if you're experiencing difficulties
    private void OnReceivedClientData(object? sender, EventArgs e)
    {
        _netConfig.ReceivedInitialNwVars -= OnReceivedClientData;

        if (_credentials is not { } creds)
            return;

        _player.SetLocalSession(_player.CreateAndAddSession(creds.UserId, creds.Username));
    }

    #endregion

    #region Private API

    private void TryAuthorize()
    {
        if (!TryReadToken(out var savedToken))
        {
            RequestNewSession();
            return;
        }

        var loginMsg = new MsgEPALogin
        {
            Token = savedToken
        };

        _net.ClientSendMessage(loginMsg);
    }

    private void RequestNewSession()
    {
        var newMsg = new MsgEPACreateSession();
        _net.ClientSendMessage(newMsg);
    }

    private bool TryReadToken([NotNullWhen(true)] out string? token)
    {
        token = null;
        try
        {
            using var stream = _resource.UserData.OpenRead(BaseDataPath / DataFilePath);
            using var reader = new StreamReader(stream, EncodingHelpers.UTF8);
            var buffer = new char[DataFileSizeLimit];
            var data = reader.Read(buffer, 0, DataFileSizeLimit);
            token = new string(buffer, 0, data);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Unexpected error occured during attempt to read token:\n{ex.ToStringBetter()}");
            return false;
        }
    }

    private bool TryWriteToken(string token)
    {
        try
        {
            using var stream = _resource.UserData.OpenWriteText(BaseDataPath / DataFilePath);
            stream.Write(token);
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error occured during attempt to save token:\n{ex.ToStringBetter()}");
            return false;
        }
    }

    #endregion

    #region Public API

    /// <inheritdoc />
    public void CheckAuthState()
    {
        DebugTools.Assert(_credentials == null, "Tried to check auth state when already authorized");

        if (_credentials != null)
            return;

        _net.ClientSendMessage(new MsgEPACheckSession());
    }

    #endregion
}
