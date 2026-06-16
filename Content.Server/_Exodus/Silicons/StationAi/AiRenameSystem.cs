using Content.Server.Administration.Logs;
using Content.Server.EUI;
using Content.Shared._Exodus.Silicons.StationAi;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.NameIdentifier;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Silicons.StationAi;

public sealed class AiRenameSystem : EntitySystem
{
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedStationAiSystem _stationAi = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    private readonly Dictionary<EntityUid, AiRenameEui> _openEuis = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationAiHeldComponent, AiRenameEvent>(OnAiRename);
        SubscribeLocalEvent<StationAiHeldComponent, TransformSpeakerNameEvent>(OnTransformSpeakerName);
        SubscribeLocalEvent<StationAiHeldComponent, ComponentShutdown>(OnHeldShutdown);
    }

    private void OnAiRename(Entity<StationAiHeldComponent> ent, ref AiRenameEvent args)
    {
        if (!TryComp<ActorComponent>(ent.Owner, out var actor))
            return;

        if (!_stationAi.TryGetCore(ent.Owner, out _))
            return;

        args.Handled = true;

        // Only one rename window per shell at a time.
        if (_openEuis.Remove(ent.Owner, out var existing))
            existing.Close();

        var currentBase = GetBaseName(ent.Owner);

        var eui = new AiRenameEui(this, ent.Owner, currentBase);
        _openEuis[ent.Owner] = eui;
        _eui.OpenEui(eui, actor.PlayerSession);
    }

    private void OnHeldShutdown(Entity<StationAiHeldComponent> ent, ref ComponentShutdown args)
    {
        if (_openEuis.Remove(ent.Owner, out var eui))
            eui.Close();
    }

    public void NotifyEuiClosed(EntityUid heldUid, AiRenameEui eui)
    {
        if (_openEuis.TryGetValue(heldUid, out var current) && current == eui)
            _openEuis.Remove(heldUid);
    }

    private void OnTransformSpeakerName(Entity<StationAiHeldComponent> ent, ref TransformSpeakerNameEvent args)
    {
        if (!_stationAi.TryGetCore(ent.Owner, out var core))
            return;

        args.VoiceName = Name(core.Owner);
    }

    public void RenameCore(EntityUid heldUid, string newName, ICommonSession? renamer = null)
    {
        if (!_stationAi.TryGetCore(heldUid, out var core))
            return;

        var saved = EnsureComp<AiRenameNameComponent>(heldUid);
        saved.BaseName = newName;
        saved.Identifier = GetIdentifier(heldUid);
        Dirty(heldUid, saved);

        var finalName = _stationAi.BuildAiRenameFullName(saved.BaseName, saved.Identifier);
        var oldName = MetaData(core.Owner).EntityName;

        _metaData.SetEntityName(core.Owner, finalName);
        _metaData.SetEntityName(heldUid, finalName);

        if (renamer != null)
        {
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{renamer:player} renamed AI core {ToPrettyString(core.Owner):target} from \"{oldName}\" to \"{finalName}\"");
        }
    }

    /// <summary>
    /// Returns the editable part of the AI name.
    /// </summary>
    private string GetBaseName(EntityUid heldUid)
    {
        if (TryComp<AiRenameNameComponent>(heldUid, out var saved) &&
            !string.IsNullOrEmpty(saved.BaseName))
            return saved.BaseName;

        return GetEditableBaseName(heldUid);
    }

    private string GetEditableBaseName(EntityUid uid)
    {
        var name = MetaData(uid).EntityName;
        var identifier = GetIdentifier(uid);
        if (string.IsNullOrEmpty(identifier))
            return name;

        var suffix = $" {identifier}";
        return name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
    }

    private string GetIdentifier(EntityUid uid)
    {
        return TryComp<NameIdentifierComponent>(uid, out var identifier)
            ? identifier.FullIdentifier
            : string.Empty;
    }
}
