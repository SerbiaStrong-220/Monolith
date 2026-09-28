using Content.Server._Exodus.War;
using Content.Server._NF.SectorServices; // Frontier
using Content.Server.Administration.Logs;
using Content.Server.AlertLevel;
using Content.Server.Chat.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Popups;
using Content.Server.RoundEnd;
using Content.Server.Screens.Components;
using Content.Shared._Exodus.Communications;
using Content.Shared._Exodus.War;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.SS220.TTS;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Communications;

public sealed partial class CommunicationsConsoleSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private AlertLevelSystem _alertLevelSystem = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private DeviceNetworkSystem _deviceNetworkSystem = default!;
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!; // Frontier: sector-wide alerts
    [Dependency] private FactionAlertLevelSystem _factionAlerts = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(5);

    public override void Initialize()
    {
        // All events that refresh the BUI
        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
        SubscribeLocalEvent<RoundEndSystemChangedEvent>(_ => OnGenericBroadcastEvent());
        SubscribeLocalEvent<AlertLevelDelayFinishedEvent>(_ => OnGenericBroadcastEvent());
        SubscribeLocalEvent<FactionAlertLevelChangedEvent>(OnFactionAlertLevelChanged);
        SubscribeLocalEvent<SectorCodeTransitionChangedEvent>(OnSectorCodeTransitionChanged);
        SubscribeLocalEvent<CommunicationsConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);

        // Messages from the BUI
        SubscribeLocalEvent<CommunicationsConsoleComponent, CommunicationsConsoleSelectAlertLevelMessage>(OnSelectAlertLevelMessage);
        SubscribeLocalEvent<CommunicationsConsoleComponent, CommunicationsConsoleAnnounceMessage>(OnAnnounceMessage);
        SubscribeLocalEvent<CommunicationsConsoleComponent, CommunicationsConsoleBroadcastMessage>(OnBroadcastMessage);

        // On console init, set cooldown
        SubscribeLocalEvent<CommunicationsConsoleComponent, MapInitEvent>(OnCommunicationsConsoleMapInit);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<CommunicationsConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime < comp.NextUiUpdate)
                continue;

            comp.NextUiUpdate += UiUpdateInterval;

            if (_uiSystem.IsUiOpen(uid, CommunicationsConsoleUiKey.Key))
                UpdateCommsConsoleInterface((uid, comp));
        }

        base.Update(frameTime);
    }

    private void OnCommunicationsConsoleMapInit(Entity<CommunicationsConsoleComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextAnnouncementAt = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.InitialDelay);
        ent.Comp.NextUiUpdate = _timing.CurTime + UiUpdateInterval;
    }

    private void OnGenericBroadcastEvent()
    {
        UpdateCommsConsoleInterface();
    }

    private void OnAlertLevelChanged(AlertLevelChangedEvent args)
    {
        UpdateCommsConsoleInterface();
    }

    /// <summary>
    /// Updates the UI for all comms consoles.
    /// </summary>
    public void UpdateCommsConsoleInterface()
    {
        var query = EntityQueryEnumerator<CommunicationsConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_uiSystem.IsUiOpen(uid, CommunicationsConsoleUiKey.Key))
                UpdateCommsConsoleInterface((uid, comp));
        }
    }

    /// <summary>
    /// Updates the UI for a particular comms console.
    /// </summary>
    public void UpdateCommsConsoleInterface(Entity<CommunicationsConsoleComponent> ent)
    {
        var (uid, comp) = ent;
        var stationUid = _sectorService.GetServiceEntity(); // Frontier: sector-wide alerts
        List<string>? levels = null;
        var currentLevel = string.Empty;
        float currentDelay = 0;
        string? pendingAlert = null;
        var pendingAlertAt = TimeSpan.Zero;
        var currentAlertColor = Color.White;

        if (stationUid.Valid) // Frontier: != null < .Valid
        {
            if (TryComp(stationUid, out AlertLevelComponent? alertComp) && // Frontier: stationUid.Value<stationUid
                alertComp.AlertLevels != null)
            {
                if (alertComp.IsSelectable && comp.CanSetAlertLevel)
                {
                    levels = new();
                    foreach (var (id, detail) in alertComp.AlertLevels.Levels)
                    {
                        if (detail.Selectable)
                        {
                            levels.Add(id);
                        }
                    }
                }

                currentLevel = alertComp.CurrentLevel;
                currentDelay = _alertLevelSystem.GetAlertLevelDelay(stationUid, alertComp); // Frontier: stationUid.Value<stationUid
                pendingAlert = alertComp.PendingLevel;
                pendingAlertAt = alertComp.PendingAt;
                if (alertComp.AlertLevels.Levels.TryGetValue(currentLevel, out var currentDetail))
                    currentAlertColor = currentDetail.Color;
            }
        }

        FactionAlertLevelState? factionAlerts = null;
        if (HasComp<WarDeclarationConsoleComponent>(uid))
            _factionAlerts.TryCopyState(out factionAlerts);

        _uiSystem.SetUiState(uid, CommunicationsConsoleUiKey.Key, new CommunicationsConsoleInterfaceState(
            CanAnnounce(comp),
            levels,
            currentLevel,
            currentDelay,
            BuildWarDeclarationState(uid),
            factionAlerts,
            pendingAlert,
            pendingAlertAt,
            currentAlertColor
        ));
    }

    private bool CanAnnounce(CommunicationsConsoleComponent comp)
    {
        return _timing.CurTime >= comp.NextAnnouncementAt;
    }

    private bool CanUse(EntityUid user, EntityUid console)
    {
        if (TryComp<AccessReaderComponent>(console, out var accessReaderComponent))
        {
            return _accessReaderSystem.IsAllowed(user, console, accessReaderComponent);
        }
        return true;
    }

    private void OnSelectAlertLevelMessage(Entity<CommunicationsConsoleComponent> ent, ref CommunicationsConsoleSelectAlertLevelMessage message)
    {
        var (uid, comp) = ent;
        if (message.Actor is not { Valid: true } mob)
            return;

        if (!comp.CanSetAlertLevel || string.IsNullOrWhiteSpace(message.Level))
            return;

        if (!CanUse(mob, uid))
        {
            _popupSystem.PopupCursor(Loc.GetString("comms-console-permission-denied"), message.Actor, PopupType.Medium);
            return;
        }

        var sector = _sectorService.GetServiceEntity();
        if (sector.Valid)
            _alertLevelSystem.SetLevel(sector, message.Level, true, true);
    }

    private void OnUiOpened(Entity<CommunicationsConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateCommsConsoleInterface(ent);
    }

    private void OnSectorCodeTransitionChanged(ref SectorCodeTransitionChangedEvent args)
    {
        OnGenericBroadcastEvent();
    }

    private void OnFactionAlertLevelChanged(ref FactionAlertLevelChangedEvent args)
    {
        OnGenericBroadcastEvent();
    }

    private void OnAnnounceMessage(Entity<CommunicationsConsoleComponent> ent, ref CommunicationsConsoleAnnounceMessage message)
    {
        var (uid, comp) = ent;
        var maxLength = _cfg.GetCVar(CCVars.ChatMaxAnnouncementLength);
        var msg = SharedChatSystem.SanitizeAnnouncement(message.Message, maxLength);
        if (string.IsNullOrWhiteSpace(msg))
            return;

        var author = Loc.GetString("comms-console-announcement-unknown-sender");
        var voiceId = string.Empty;
        if (message.Actor is { Valid: true } mob)
        {
            if (!CanAnnounce(comp))
            {
                return;
            }

            if (!CanUse(mob, uid))
            {
                _popupSystem.PopupEntity(Loc.GetString("comms-console-permission-denied"), uid, message.Actor);
                return;
            }

            var tryGetIdentityShortInfoEvent = new TryGetIdentityShortInfoEvent(uid, mob);
            RaiseLocalEvent(tryGetIdentityShortInfoEvent);
            author = tryGetIdentityShortInfoEvent.Title;

            if (TryComp<TTSComponent>(mob, out var tts))
            {
                voiceId = tts.VoicePrototypeId;
            }
        }

        comp.NextAnnouncementAt = _timing.CurTime + TimeSpan.FromSeconds(comp.Delay);
        UpdateCommsConsoleInterface((uid, comp));

        var ev = new CommunicationConsoleAnnouncementEvent(uid, comp, msg, message.Actor);
        RaiseLocalEvent(ref ev);

        // allow admemes with vv
        Loc.TryGetString(comp.Title, out var title);
        title ??= comp.Title;

        msg += "\n" + Loc.GetString("comms-console-announcement-sent-by") + " " + author;
        if (comp.Global)
        {
            _chatSystem.DispatchGlobalAnnouncement(msg, title, announcementSound: comp.Sound, colorOverride: comp.Color);

            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"{ToPrettyString(message.Actor):player} has sent the following global announcement: {msg}");
            return;
        }

        _chatSystem.DispatchStationAnnouncement(uid, msg, title, colorOverride: comp.Color, voiceId: voiceId);

        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"{ToPrettyString(message.Actor):player} has sent the following station announcement: {msg}");
    }

    private void OnBroadcastMessage(Entity<CommunicationsConsoleComponent> ent, ref CommunicationsConsoleBroadcastMessage message)
    {
        var (uid, component) = ent;
        if (!TryComp<DeviceNetworkComponent>(uid, out var net))
            return;

        // Frontier: check access for broadcast
        if (message.Actor is { Valid: true } mob)
        {
            if (!CanUse(mob, uid))
            {
                _popupSystem.PopupEntity(Loc.GetString("comms-console-permission-denied"), uid, message.Actor);
                return;
            }
        }
        // End Frontier

        var payload = new NetworkPayload
        {
            [ScreenMasks.Text] = message.Message
        };

        _deviceNetworkSystem.QueuePacket(uid, null, payload, net.TransmitFrequency);

        _adminLogger.Add(LogType.DeviceNetwork, LogImpact.Low, $"{ToPrettyString(message.Actor):player} has sent the following broadcast: {message.Message:msg}");
    }
}

/// <summary>
/// Raised on announcement
/// </summary>
[ByRefEvent]
public record struct CommunicationConsoleAnnouncementEvent(EntityUid Uid, CommunicationsConsoleComponent Component, string Text, EntityUid? Sender)
{
    public EntityUid Uid = Uid;
    public CommunicationsConsoleComponent Component = Component;
    public EntityUid? Sender = Sender;
    public string Text = Text;
}
