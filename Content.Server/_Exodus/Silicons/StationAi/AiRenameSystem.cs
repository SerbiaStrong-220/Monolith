using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Shared._Exodus.Silicons.StationAi;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Containers;
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

    private readonly Dictionary<EntityUid, AiRenameEui> _openEuis = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationAiHeldComponent, AiRenameEvent>(OnAiRename);
        SubscribeLocalEvent<StationAiHeldComponent, TransformSpeakerNameEvent>(OnTransformSpeakerName);
        SubscribeLocalEvent<StationAiHeldComponent, ComponentShutdown>(OnHeldShutdown);

        // Run after upstream's OnAiInsert (which copies brain name to core) so we can
        // re-apply the player's chosen name on top, surviving Intellicard transfers.
        // Broadcast subscription instead of per-component to avoid the
        // "Duplicate Subscriptions" conflict with upstream's StationAiCoreComponent handler.
        SubscribeLocalEvent<EntInsertedIntoContainerMessage>(
            OnCoreInsert,
            after: new[] { typeof(SharedStationAiSystem) });
    }

    private void OnAiRename(Entity<StationAiHeldComponent> ent, ref AiRenameEvent args)
    {
        if (!TryComp<ActorComponent>(ent.Owner, out var actor))
            return;

        if (!_stationAi.TryGetCore(ent.Owner, out _))
            return;

        var now = _timing.CurTime;
        if (TryComp<AiRenameCooldownComponent>(ent.Owner, out var cooldown) && now < cooldown.NextRenameAt)
        {
            var remaining = (int)(cooldown.NextRenameAt - now).TotalSeconds;
            _chat.DispatchServerMessage(actor.PlayerSession,
                Loc.GetString("ai-rename-cooldown", ("seconds", remaining)));
            return;
        }

        // Only one rename window per shell at a time — closing the previous prevents
        // the player from queueing two confirmations and bypassing the cooldown.
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

    /// <summary>
    /// Re-applies the saved custom name to the core after upstream's OnAiInsert
    /// copies the raw brain name into the core. This is what makes the rename
    /// survive an Intellicard pull-and-reinsert.
    /// </summary>
    private void OnCoreInsert(EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != StationAiCoreComponent.Container)
            return;

        if (!HasComp<StationAiCoreComponent>(args.Container.Owner))
            return;

        if (!TryComp<AiRenameBaseNameComponent>(args.Entity, out var saved) ||
            string.IsNullOrEmpty(saved.BaseName))
            return;

        var finalName = BuildFullName(saved.BaseName, MetaData(args.Entity).EntityName);

        _metaData.SetEntityName(args.Container.Owner, finalName);
    }

    public void RenameCore(EntityUid heldUid, string newName, ICommonSession? renamer = null)
    {
        if (!_stationAi.TryGetCore(heldUid, out var core))
            return;

        var finalName = BuildFullName(newName, MetaData(heldUid).EntityName);

        var saved = EnsureComp<AiRenameBaseNameComponent>(heldUid);
        var oldName = MetaData(core.Owner).EntityName;
        saved.BaseName = newName;
        Dirty(heldUid, saved);

        _metaData.SetEntityName(core.Owner, finalName);
        _metaData.SetEntityName(heldUid, finalName);

        if (renamer != null)
        {
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{renamer:player} renamed AI core {ToPrettyString(core.Owner):target} from \"{oldName}\" to \"{finalName}\"");
        }
    }

    public void ApplyCooldown(EntityUid heldUid)
    {
        var cooldown = EnsureComp<AiRenameCooldownComponent>(heldUid);
        cooldown.NextRenameAt = _timing.CurTime + RenameCooldown;
        Dirty(heldUid, cooldown);
    }

    /// <summary>
    /// Returns the editable part of the brain name: the saved base name if a player has renamed
    /// before, otherwise the current brain name with the trailing " (IDENTIFIER)" stripped.
    /// </summary>
    private string GetBaseName(EntityUid heldUid)
    {
        if (TryComp<AiRenameBaseNameComponent>(heldUid, out var saved) && !string.IsNullOrEmpty(saved.BaseName))
            return saved.BaseName;

        var fullName = MetaData(heldUid).EntityName;
        var identifier = ParseTrailingIdentifier(fullName);

        if (string.IsNullOrEmpty(identifier))
            return fullName;

        var suffix = $" ({identifier})";
        return fullName.EndsWith(suffix, StringComparison.Ordinal)
            ? fullName[..^suffix.Length]
            : fullName;
    }

    /// <summary>
    /// Builds the displayed AI name from a base name and a source name that may carry
    /// a trailing " (IDENTIFIER)" suffix. Single source of truth for the rename format.
    /// </summary>
    private static string BuildFullName(string baseName, string identifierSource)
    {
        var identifier = ParseTrailingIdentifier(identifierSource);
        return string.IsNullOrEmpty(identifier)
            ? baseName
            : $"{baseName} ({identifier})";
    }

    private static string ParseTrailingIdentifier(string name)
    {
        if (!name.EndsWith(')'))
            return string.Empty;

        var open = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0)
            return string.Empty;

        return name[(open + 2)..^1];
    }
}
