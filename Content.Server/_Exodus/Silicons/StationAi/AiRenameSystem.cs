using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Shared._Exodus.Silicons.StationAi;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Silicons.StationAi;

public sealed class AiRenameSystem : EntitySystem
{
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedStationAiSystem _stationAi = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;

    private static readonly TimeSpan RenameCooldown = TimeSpan.FromMinutes(1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationAiHeldComponent, AiRenameEvent>(OnAiRename);
        SubscribeLocalEvent<StationAiHeldComponent, TransformSpeakerNameEvent>(OnTransformSpeakerName);
    }

    private void OnAiRename(Entity<StationAiHeldComponent> ent, ref AiRenameEvent args)
    {
        if (!TryComp<ActorComponent>(ent.Owner, out var actor))
            return;

        if (!_stationAi.TryGetCore(ent.Owner, out var core))
            return;

        var now = _timing.CurTime;
        if (TryComp<AiRenameCooldownComponent>(ent.Owner, out var cooldown) && now < cooldown.NextRenameAt)
        {
            var remaining = (int)(cooldown.NextRenameAt - now).TotalSeconds;
            _chat.DispatchServerMessage(actor.PlayerSession,
                Loc.GetString("ai-rename-cooldown", ("seconds", remaining)));
            return;
        }

        var currentName = GetBaseName(core.Owner);

        var eui = new AiRenameEui(this, core.Owner, ent.Owner, currentName);
        _eui.OpenEui(eui, actor.PlayerSession);
    }

    private void OnTransformSpeakerName(Entity<StationAiHeldComponent> ent, ref TransformSpeakerNameEvent args)
    {
        if (!_stationAi.TryGetCore(ent.Owner, out var core))
            return;

        args.VoiceName = Name(core.Owner);
    }

    public void RenameCore(EntityUid coreUid, string newName, ICommonSession? renamer = null)
    {
        var identifier = EnsureIdentifier(coreUid);
        var oldName = MetaData(coreUid).EntityName;
        var finalName = string.IsNullOrEmpty(identifier) ? newName : $"{newName} ({identifier})";

        _metaData.SetEntityName(coreUid, finalName);

        if (renamer != null)
        {
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{renamer:player} renamed AI core {ToPrettyString(coreUid):target} from \"{oldName}\" to \"{finalName}\"");
        }
    }

    public void ApplyCooldown(EntityUid heldUid)
    {
        var cooldown = EnsureComp<AiRenameCooldownComponent>(heldUid);
        cooldown.NextRenameAt = _timing.CurTime + RenameCooldown;
        Dirty(heldUid, cooldown);
    }

    /// <summary>
    /// Returns the editable part of the core name, stripping the trailing " (IDENTIFIER)" suffix
    /// using the cached identifier component. The component is created on first access if missing.
    /// </summary>
    private string GetBaseName(EntityUid coreUid)
    {
        var fullName = MetaData(coreUid).EntityName;
        var identifier = EnsureIdentifier(coreUid);

        if (string.IsNullOrEmpty(identifier))
            return fullName;

        var suffix = $" ({identifier})";
        return fullName.EndsWith(suffix, StringComparison.Ordinal)
            ? fullName[..^suffix.Length]
            : fullName;
    }

    /// <summary>
    /// One-time migration: parse the identifier from the existing entity name and store it.
    /// Used when the core was spawned with a RandomMetadata-generated " (XXX)" suffix
    /// before this system tracked identifiers explicitly.
    /// </summary>
    private string EnsureIdentifier(EntityUid coreUid)
    {
        if (TryComp<AiRenameIdentifierComponent>(coreUid, out var existing))
            return existing.Identifier;

        var fullName = MetaData(coreUid).EntityName;
        var parsed = ParseTrailingIdentifier(fullName);

        var comp = AddComp<AiRenameIdentifierComponent>(coreUid);
        comp.Identifier = parsed;
        Dirty(coreUid, comp);
        return parsed;
    }

    private static string ParseTrailingIdentifier(string name)
    {
        if (!name.EndsWith(')'))
            return string.Empty;

        var open = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0)
            return string.Empty;

        var inner = name[(open + 2)..^1];
        return inner;
    }
}
