using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Shared._Exodus.Silicons.StationAi;
using Content.Shared.Chat;
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

    private static readonly TimeSpan RenameCooldown = TimeSpan.FromMinutes(1);

    private readonly Dictionary<EntityUid, TimeSpan> _cooldowns = new();

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

        if (!_stationAi.TryGetCore(ent.Owner, out var core) || core.Owner == EntityUid.Invalid)
            return;

        var now = _timing.CurTime;
        if (_cooldowns.TryGetValue(ent.Owner, out var nextAllowed) && now < nextAllowed)
        {
            var remaining = (int)(nextAllowed - now).TotalSeconds;
            _chat.DispatchServerMessage(actor.PlayerSession,
                Loc.GetString("ai-rename-cooldown", ("seconds", remaining)));
            return;
        }

        // Show only the editable part — strip trailing " (IDENTIFIER)"
        var currentName = MetaData(core.Owner).EntityName;
        var parenIdx = currentName.LastIndexOf(" (", StringComparison.Ordinal);
        if (parenIdx > 0 && currentName.EndsWith(")"))
            currentName = currentName[..parenIdx];

        var eui = new AiRenameEui(this, core.Owner, ent.Owner, currentName);
        _eui.OpenEui(eui, actor.PlayerSession);
    }

    private void OnTransformSpeakerName(Entity<StationAiHeldComponent> ent, ref TransformSpeakerNameEvent args)
    {
        if (!_stationAi.TryGetCore(ent.Owner, out var core) || core.Owner == EntityUid.Invalid)
            return;

        args.VoiceName = Name(core.Owner);
    }

    public void RenameCore(EntityUid coreUid, string newName)
    {
        // Preserve existing " (IDENTIFIER)" suffix
        var currentName = MetaData(coreUid).EntityName;
        var parenIdx = currentName.LastIndexOf(" (", StringComparison.Ordinal);
        var suffix = parenIdx > 0 && currentName.EndsWith(")")
            ? currentName[parenIdx..]
            : string.Empty;

        _metaData.SetEntityName(coreUid, newName + suffix);
    }

    public void ApplyCooldown(EntityUid heldUid)
    {
        _cooldowns[heldUid] = _timing.CurTime + RenameCooldown;
    }
}
