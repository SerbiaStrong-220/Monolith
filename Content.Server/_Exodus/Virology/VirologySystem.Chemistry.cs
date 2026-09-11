// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared._Exodus.Virology;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;

    private readonly Dictionary<string, VirusMutationPrototype> _mutations = [];
    private readonly Dictionary<string, VirusRevealPrototype> _reveals = [];
    private readonly Dictionary<string, VirusSymptomRemovalPrototype> _removals = [];

    private void InitializeChemistry()
    {
        BuildTables();
        SubscribeLocalEvent<VirusReactiveComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
    }

    private void BuildTables()
    {
        _mutations.Clear();
        foreach (var proto in _proto.EnumeratePrototypes<VirusMutationPrototype>())
            _mutations[proto.Mutagen] = proto;

        _reveals.Clear();
        foreach (var proto in _proto.EnumeratePrototypes<VirusRevealPrototype>())
            _reveals[proto.Reagent] = proto;

        _removals.Clear();
        foreach (var proto in _proto.EnumeratePrototypes<VirusSymptomRemovalPrototype>())
            _removals[proto.Reagent] = proto;
    }

    private void OnSolutionChanged(Entity<VirusReactiveComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (ent.Comp.Reacting || !_solutionContainer.TryGetSolution(ent.Owner, args.SolutionId, out var solution, out _))
            return;

        ent.Comp.Reacting = true;
        try
        {
            React(solution.Value);
        }
        finally
        {
            ent.Comp.Reacting = false;
        }
    }

    private void React(Entity<SolutionComponent> ent)
    {
        var solution = ent.Comp.Solution;
        VirusMutationPrototype? mutation = null;
        VirusRevealPrototype? reveal = null;
        VirusSymptomRemovalPrototype? removal = null;
        var hasVirus = false;
        foreach (var quantity in solution.Contents)
        {
            if (VirusData.From(quantity.Reagent) is { Viruses.Count: > 0 })
            {
                hasVirus = true;
                continue;
            }

            if (_mutations.TryGetValue(quantity.Reagent.Prototype, out var m) && m.Cost > FixedPoint2.Zero && quantity.Quantity >= m.Cost)
                mutation ??= m;
            if (_reveals.TryGetValue(quantity.Reagent.Prototype, out var r) && r.Amount > FixedPoint2.Zero && quantity.Quantity >= r.Amount)
                reveal ??= r;
            if (_removals.TryGetValue(quantity.Reagent.Prototype, out var rm) && rm.Amount > FixedPoint2.Zero && quantity.Quantity >= rm.Amount)
                removal ??= rm;
        }

        if (!hasVirus || (mutation == null && reveal == null && removal == null))
            return;

        // Split/clone in the host chemistry system shares reagent metadata. Detach it before any mutation.
        var viruses = new List<VirusDescriptor>();
        for (var i = 0; i < solution.Contents.Count; i++)
        {
            var quantity = solution.Contents[i];
            if (VirusData.From(quantity.Reagent) is not { Viruses.Count: > 0 })
                continue;

            var data = new List<ReagentData>();
            foreach (var entry in quantity.Reagent.Data!)
            {
                var clone = entry.Clone();
                data.Add(clone);
                if (clone is VirusData clonedVirus)
                    viruses.AddRange(clonedVirus.Viruses);
            }

            solution.Contents[i] = new ReagentQuantity(new ReagentId(quantity.Reagent.Prototype, data), quantity.Quantity);
        }

        var mutated = false;
        var revealed = false;
        var removed = false;
        foreach (var virus in viruses)
        {
            if (mutation is { Pool.Count: > 0 })
            {
                var reagent = new ReagentId(mutation.Mutagen, null);
                while (solution.GetReagentQuantity(reagent) >= mutation.Cost && TryMutate(virus, mutation))
                {
                    solution.RemoveReagent(reagent, mutation.Cost);
                    mutated = true;
                }
            }

            if (reveal != null)
            {
                var reagent = new ReagentId(reveal.Reagent, null);
                while (solution.GetReagentQuantity(reagent) >= reveal.Amount && TryReveal(virus, reveal.Genome))
                {
                    solution.RemoveReagent(reagent, reveal.Amount);
                    revealed = true;
                }
            }

            if (removal != null)
            {
                var reagent = new ReagentId(removal.Reagent, null);
                while (solution.GetReagentQuantity(reagent) >= removal.Amount && TryRemoveSymptom(virus))
                {
                    solution.RemoveReagent(reagent, removal.Amount);
                    removed = true;
                }
            }
        }

        if (mutated || revealed || removed)
            _solutionContainer.UpdateChemicals(ent);

        if (mutated && mutation?.MutateSound is { } mutationSound)
            _audio.PlayPvs(mutationSound, ent);
        if (revealed && reveal?.RevealSound is { } revealSound)
            _audio.PlayPvs(revealSound, ent);
        if (removed && removal?.RemoveSound is { } removalSound)
            _audio.PlayPvs(removalSound, ent);
    }
}
