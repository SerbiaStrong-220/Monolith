using Content.Shared._Exodus.Genetics;
using Content.Shared._Exodus.Virology;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;

namespace Content.Server._Exodus.Genetics;

public sealed class GeneticInjectorSystem : EntitySystem
{
    [Dependency] private readonly GeneticsSystem _genetics = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticInjectorComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<GeneticInjectorComponent, GeneticInjectionDoAfterEvent>(OnInjected);
    }

    private bool CanInject(EntityUid target, EntityUid user)
    {
        var attempt = new VirusInjectionAttemptEvent();
        RaiseLocalEvent(target, ref attempt);
        if (attempt.Cancelled && attempt.Message is { } message)
            _popup.PopupEntity(message, target, user);
        return !attempt.Cancelled;
    }

    private void OnInteract(Entity<GeneticInjectorComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || ent.Comp.Used || args.Target is not { } target ||
            !_genetics.TryGetLivingGenome(target, out var genome))
            return;
        args.Handled = true;
        if (!PrepareSample(ent, genome))
        {
            _popup.PopupEntity(Loc.GetString("genetics-invalid-sample"), ent, args.User);
            return;
        }
        if (!CanInject(target, args.User))
            return;
        var ev = new GeneticInjectionDoAfterEvent { Revision = genome.Revision };
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.InjectionTime, ev, ent, target, ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private bool PrepareSample(Entity<GeneticInjectorComponent> ent, GenomeComponent genome)
    {
        if (ent.Comp.Mutation is { } mutation)
        {
            var round = _genetics.GetRound();
            ent.Comp.Context = round.Context;
            ent.Comp.Block = round.Mutations.IndexOf(mutation);
            ent.Comp.Value = GeneticsSystem.MaxBlockValue;
        }
        if (ent.Comp.Context != genome.Context)
            return false;
        return ent.Comp.Sample is { } sample
            ? _genetics.IsCompatible(sample)
            : ent.Comp.Block >= 0 && ent.Comp.Block < genome.Blocks.Count && ent.Comp.Value <= GeneticsSystem.MaxBlockValue;
    }

    private void OnInjected(Entity<GeneticInjectorComponent> ent, ref GeneticInjectionDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Used || args.Target is not { } target ||
            TerminatingOrDeleted(ent) || TerminatingOrDeleted(target) || TerminatingOrDeleted(args.User) ||
            !_interaction.IsAccessible(args.User, target) || !_interaction.InRangeUnobstructed(args.User, target) ||
            !_genetics.TryGetLivingGenome(target, out var genome) || !PrepareSample(ent, genome) ||
            genome.Revision != args.Revision || !CanInject(target, args.User))
            return;

        ent.Comp.Used = true;
        var applied = ent.Comp.Sample is { } sample
            ? _genetics.TryApply((target, genome), sample, args.User)
            : _genetics.TrySetBlock((target, genome), ent.Comp.Block, ent.Comp.Value, args.User);
        if (!applied)
        {
            ent.Comp.Used = false;
            return;
        }
        args.Handled = true;
        _damage.TryChangeDamage(target, ent.Comp.InjectionDamage, true, false, ignoreGlobalModifiers: true);
        _popup.PopupEntity(Loc.GetString("genetics-injected"), target, args.User);
        QueueDel(ent);
    }
}
