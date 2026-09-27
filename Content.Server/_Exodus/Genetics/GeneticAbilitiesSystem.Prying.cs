using Content.Server.Administration.Logs;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Prying.Components;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly IAdminLogManager _admin = default!;

    private void InitializePrying()
    {
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticPryEvent>(OnPry);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticPryDoAfterEvent>(OnPried);
    }

    private bool CanPry(EntityUid user, EntityUid target)
    {
        if (!CanUse(user, GeneticAbility.ForcePry) || TerminatingOrDeleted(target) ||
            !_interaction.IsAccessible(user, target) || !_interaction.InRangeUnobstructed(user, target) ||
            !HasComp<DoorComponent>(target))
            return false;
        var attempt = new BeforePryEvent(user, true, true, true);
        RaiseLocalEvent(target, ref attempt);
        return !attempt.Cancelled;
    }

    private void OnPry(Entity<GeneticEffectsComponent> ent, ref GeneticPryEvent args)
    {
        if (args.Handled || !CanPry(ent, args.Target))
            return;
        Reveal(ent);
        var state = EnsureComp<GeneticAbilityStateComponent>(ent);
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ent, state.PryTime,
            new GeneticPryDoAfterEvent(), ent, args.Target)
        {
            BreakOnMove = true, BreakOnDamage = true, DistanceThreshold = 1.5f,
        });
    }

    private void OnPried(Entity<GeneticEffectsComponent> ent, ref GeneticPryDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target || !CanPry(ent, target))
            return;
        args.Handled = true;
        Reveal(ent);
        if (TryComp<GeneticAbilityStateComponent>(ent, out var state))
            _audio.PlayPvs(state.PrySound, target);
        var ev = new PriedEvent(ent);
        RaiseLocalEvent(target, ref ev);
        _admin.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(ent):user} genetically pried {ToPrettyString(target):target}");
    }
}
