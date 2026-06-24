using Content.Server.Administration.Logs;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants.Components;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Exodus.Implants;

public sealed partial class TransferSolutionOnTriggerSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private ReactiveSystem _reactiveSystem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TransferSolutionOnTriggerComponent, TriggerEvent>(OnTriggered);
    }

    public bool InjectSolution(EntityUid user, Entity<TransferSolutionOnTriggerComponent> implant, string solutionName, float transferAmount)
    {
        if (!_solutionContainer.TryGetSolution(implant.Owner, solutionName, out var initialSoln, out var initialSolution))
        {
            Log.Error($"Couldn't find solution named {solutionName} in entity {ToPrettyString(implant.Owner)}");
            return false;
        }

        if (!_solutionContainer.TryGetInjectableSolution(user, out var targetSoln, out var targetSolution))
        {
            _popup.PopupEntity(Loc.GetString("inject-trigger-cant-inject-message", ("target", Identity.Entity(user, EntityManager))), user, user);
            return false;
        }

        var realTransferAmount = FixedPoint2.Min(initialSolution.Volume, targetSolution.AvailableVolume, transferAmount);
        if (realTransferAmount <= 0)
        {
            _popup.PopupEntity(Loc.GetString("inject-trigger-empty-capsule-message"), user, user);
            return false;
        }

        var removedSolution = _solutionContainer.SplitSolution(initialSoln.Value, realTransferAmount);
        if (!targetSolution.CanAddSolution(removedSolution))
        {
            _popup.PopupEntity(Loc.GetString("inject-trigger-cant-inject-message", ("target", Identity.Entity(user, EntityManager))), user, user);
            return false;
        }

        _audio.PlayPvs(implant.Comp.InjectSound, user);
        _reactiveSystem.DoEntityReaction(user, removedSolution, ReactionMethod.Injection);
        _solutionContainer.TryAddSolution(targetSoln.Value, removedSolution);

        _popup.PopupEntity(Loc.GetString("inject-trigger-feel-prick-message"), user, user);
        _adminLogger.Add(LogType.ForceFeed, $"{ToPrettyString(user):user} used inject implant with a solution {SharedSolutionContainerSystem.ToPrettyString(removedSolution):removedSolution}");

        return true;
    }

    private void OnTriggered(Entity<TransferSolutionOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (!TryComp<SubdermalImplantComponent>(ent, out var implantComp) || implantComp.ImplantedEntity is not { } user)
            return;

        if (ent.Comp.Solutions.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("inject-trigger-empty-message"), user, user);
            return;
        }

        foreach (var solData in ent.Comp.Solutions)
        {
            if (solData.UsedCount >= solData.Charges)
                continue;

            if (InjectSolution(user, ent, solData.Name, solData.TransferAmount))
                solData.UsedCount += 1;
            break;
        }

        args.Handled = true;
    }
}
