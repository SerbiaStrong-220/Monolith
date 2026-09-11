// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared._Exodus.Virology;
using Robust.Shared.Prototypes;

using Content.Server.Body.Components;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;

    private static readonly TimeSpan SuppressDuration = TimeSpan.FromMinutes(15);
    private static readonly FixedPoint2 MinCureReagentAmount = 5;
    private static readonly FixedPoint2 MinVaccineAmount = 5;
    private readonly List<VirusBroadReagentPrototype> _broadReagents = [];
    private readonly Dictionary<string, FixedPoint2> _vaccineTotals = [];
    private readonly List<Solution> _bodySolutions = [];
    private readonly List<ReagentQuantity> _infectionReagents = [];

    private void InitializeInfection()
    {
        BuildBroadReagents();
        SubscribeLocalEvent<BloodstreamComponent, VirusBleedEffectEvent>(OnBleed);
        SubscribeLocalEvent<BloodstreamComponent, VirusInjectReagentEvent>(OnInjectReagent);
        SubscribeLocalEvent<BloodstreamComponent, VirusConsumeReagentEvent>(OnConsumeReagent);
    }

    private void BuildBroadReagents()
    {
        _broadReagents.Clear();
        foreach (var proto in _proto.EnumeratePrototypes<VirusBroadReagentPrototype>())
            _broadReagents.Add(proto);
    }

    private void TickInfection()
    {
        var query = EntityQueryEnumerator<VirusSusceptibleComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out _, out var blood))
        {
            if (!_solutionContainer.ResolveSolution(uid, blood.BloodSolutionName, ref blood.BloodSolution, out var bloodSolution))
                continue;

            InfectFromBlood(uid, bloodSolution);
            if (_solutionContainer.ResolveSolution(uid, blood.ChemicalSolutionName, ref blood.ChemicalSolution, out var chemicals))
                InfectFromBlood(uid, chemicals);

            if (_mobState.IsDead(uid))
                continue;

            CollectBodySolutions(uid, blood, bloodSolution);
            ApplyVaccines(uid);

            if (!TryComp<VirusHolderComponent>(uid, out var holder))
                continue;

            foreach (var broad in _broadReagents)
                ApplyBroadReagent((uid, holder), broad);

            SuppressCured((uid, holder));
        }
    }

    private void InfectFromBlood(EntityUid uid, Solution blood)
    {
        if (!HasVirus(blood))
            return;

        // Infection may replace blood metadata; retain a snapshot without allocating on every tick.
        _infectionReagents.Clear();
        _infectionReagents.AddRange(blood.Contents);
        foreach (var quantity in _infectionReagents)
            InfectFromReagent(uid, quantity.Reagent, bloodborne: true);
    }

    private void ApplyBroadReagent(Entity<VirusHolderComponent> ent, VirusBroadReagentPrototype broad)
    {
        if (ReagentInBody(broad.Reagent) < broad.Amount)
            return;

        foreach (var virus in GetStrains(ent))
        {
            if (broad.Action == VirusBroadAction.Cure)
                RemoveVirus(virus);
            else if (virus.Comp.SuppressedUntil == null)
                SuppressVirus(virus, SuppressDuration);
        }
    }

    private void SuppressCured(Entity<VirusHolderComponent> ent)
    {
        foreach (var virus in EnumerateStrains(ent.Comp))
        {
            var comp = virus.Comp;
            if (comp.SuppressedUntil != null)
                continue;

            if (comp.Cure is not { Reagents.Count: > 0 } cure)
                continue;

            var present = 0;
            foreach (var reagent in cure.Reagents)
            {
                if (ReagentInBody(reagent) >= MinCureReagentAmount)
                    present++;
            }

            // RNA needs any one cure reagent, DNA and superviruses need all at once
            var cured = comp.IsSupervirus || comp.Genome == VirusGenome.Dna
                ? present == cure.Reagents.Count
                : present > 0;

            if (cured)
                SuppressVirus(virus, SuppressDuration);
        }
    }

    private void CollectBodySolutions(EntityUid uid, BloodstreamComponent bloodstream, Solution bloodSolution)
    {
        _bodySolutions.Clear();
        _bodySolutions.Add(bloodSolution);

        if (_solutionContainer.ResolveSolution(uid, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var metabolites))
            _bodySolutions.Add(metabolites);

        if (TryComp<BodyComponent>(uid, out var body))
        {
            foreach (var (organ, _) in _body.GetBodyOrgans(uid, body))
            {
                if (TryComp<StomachComponent>(organ, out var stomach)
                    && _solutionContainer.ResolveSolution(organ, StomachSystem.DefaultSolutionName, ref stomach.Solution, out var stomachSolution))
                    _bodySolutions.Add(stomachSolution);
            }
        }
    }

    private FixedPoint2 ReagentInBody(ProtoId<ReagentPrototype> reagent)
    {
        var total = FixedPoint2.Zero;
        foreach (var solution in _bodySolutions)
            total += solution.GetTotalPrototypeQuantity(reagent);

        return total;
    }

    private void ApplyVaccines(EntityUid host)
    {
        _vaccineTotals.Clear();
        foreach (var solution in _bodySolutions)
            AccumulateVaccines(solution, _vaccineTotals);

        foreach (var (strain, total) in _vaccineTotals)
        {
            if (total < MinVaccineAmount)
                continue;

            if (AddImmunity(host, strain))
            {
                _adminLog.Add(LogType.Virology, LogImpact.Medium,
                    $"{ToPrettyString(host):target} was vaccinated against strain {strain}");
            }

            foreach (var virus in GetStrains(host))
            {
                if (GetIdentity(virus.Comp) == strain)
                    RemoveVirus(virus);
            }
        }
    }

    private static void AccumulateVaccines(Solution solution, Dictionary<string, FixedPoint2> totals)
    {
        foreach (var quantity in solution.Contents)
        {
            if (VirusVaccineData.From(quantity.Reagent) is not { Strains.Count: > 0 } vaccine)
                continue;

            foreach (var strain in vaccine.Strains)
            {
                totals.TryGetValue(strain, out var existing);
                totals[strain] = existing + quantity.Quantity;
            }
        }
    }

    private void OnBleed(Entity<BloodstreamComponent> ent, ref VirusBleedEffectEvent args)
    {
        _bloodstream.TryModifyBleedAmount(ent, args.Amount, ent.Comp);
    }

    private void OnInjectReagent(Entity<BloodstreamComponent> ent, ref VirusInjectReagentEvent args)
    {
        if (args.Amount <= FixedPoint2.Zero || _mobState.IsDead(ent))
            return;

        _bloodstream.TryAddToChemicals(ent, new Solution(args.Reagent, args.Amount), ent.Comp);
    }

    private void OnConsumeReagent(Entity<BloodstreamComponent> ent, ref VirusConsumeReagentEvent args)
    {
        if (args.Consumed || args.Amount <= FixedPoint2.Zero)
            return;

        if (!_solutionContainer.ResolveSolution(ent.Owner, ent.Comp.ChemicalSolutionName, ref ent.Comp.ChemicalSolution, out var solution)
            || solution.GetTotalPrototypeQuantity(args.Reagent) < args.Amount)
            return;

        _solutionContainer.RemoveReagent(ent.Comp.ChemicalSolution!.Value, args.Reagent, args.Amount);
        args.Consumed = true;
    }

    private static bool HasVirus(Solution blood)
    {
        foreach (var quantity in blood.Contents)
        {
            if (VirusData.From(quantity.Reagent) is { Viruses.Count: > 0 })
                return true;
        }

        return false;
    }
}
