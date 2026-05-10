using Content.Server.EUI;
using Content.Shared._Exodus.Silicons.StationAi;
using Content.Shared.Chat;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Silicons.StationAi;

public sealed class AiRenameSystem : EntitySystem
{
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedStationAiSystem _stationAi = default!;

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

        var eui = new AiRenameEui(this, core.Owner);
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
        _metaData.SetEntityName(coreUid, newName);
    }
}
