using Content.Server.Administration;
using Content.Shared._Exodus.Silicons.StationAi;
using Content.Shared.Preferences;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Silicons.StationAi;

public sealed class AiRenameSystem : EntitySystem
{
    [Dependency] private readonly QuickDialogSystem _quickDialog = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedStationAiSystem _stationAi = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationAiHeldComponent, AiRenameEvent>(OnAiRename);
    }

    private void OnAiRename(Entity<StationAiHeldComponent> ent, ref AiRenameEvent args)
    {
        if (!TryComp<ActorComponent>(ent.Owner, out var actor))
            return;

        if (!_stationAi.TryGetCore(ent.Owner, out var core) || core.Owner == EntityUid.Invalid)
            return;

        _quickDialog.OpenDialog<string>(
            actor.PlayerSession,
            Loc.GetString("ai-rename-dialog-title"),
            Loc.GetString("ai-rename-dialog-prompt"),
            newName =>
            {
                if (string.IsNullOrWhiteSpace(newName) || newName.Length > HumanoidCharacterProfile.MaxNameLength)
                    return;

                var trimmed = newName.Trim();
                _metaData.SetEntityName(core.Owner, trimmed);
            }
        );
    }
}
