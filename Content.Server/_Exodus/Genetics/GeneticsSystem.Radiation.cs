using Content.Shared._Exodus.Radiation;
using Content.Shared.Body.Components;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticsSystem
{
    private EntityQuery<GenomeComponent> _radiationGenomes;
    private EntityQuery<HumanoidAppearanceComponent> _radiationHumanoids;
    private EntityQuery<GeneticIncompatibleComponent> _radiationIncompatible;
    private EntityQuery<MobStateComponent> _radiationMobStates;

    private void InitializeRadiation()
    {
        _radiationGenomes = GetEntityQuery<GenomeComponent>();
        _radiationHumanoids = GetEntityQuery<HumanoidAppearanceComponent>();
        _radiationIncompatible = GetEntityQuery<GeneticIncompatibleComponent>();
        _radiationMobStates = GetEntityQuery<MobStateComponent>();
        SubscribeLocalEvent<BodyComponent, RadiationDamageReceivedEvent>(OnRadiationDamageReceived);
    }

    private void OnRadiationDamageReceived(Entity<BodyComponent> ent, ref RadiationDamageReceivedEvent args)
    {
        if (args.Damage <= FixedPoint2.Zero || TerminatingOrDeleted(ent) ||
            !_radiationHumanoids.HasComp(ent) || _radiationIncompatible.HasComp(ent) ||
            !_radiationMobStates.TryComp(ent, out var mob) ||
            mob.CurrentState is not (MobState.Alive or MobState.Critical))
            return;

        if (_radiationGenomes.TryComp(ent, out var genome) && genome.NextRadiationMutation > _timing.CurTime)
            return;

        var round = GetRound();
        if (round.RadiationMutationRate <= 0 || !double.IsFinite(round.RadiationMutationRate))
            return;

        // Equal total damage has the same chance of at least one mutation, regardless of radiation tick size.
        var chance = 1 - Math.Exp(-round.RadiationMutationRate * args.Damage.Double());
        if (_random.NextDouble() >= chance || !TryGetLivingGenome(ent, out genome))
            return;

        TryActivateRadiationMutation((ent.Owner, genome), round);
    }

    private bool TryActivateRadiationMutation(Entity<GenomeComponent> ent, GeneticsRoundComponent round)
    {
        var selected = -1;
        var totalWeight = 0.0;
        for (var i = 0; i < ent.Comp.Blocks.Count && i < round.Mutations.Count; i++)
        {
            if (round.Mutations[i] is not { } id || IsBlockActive(ent.Comp.Blocks[i], round.Thresholds[i]))
                continue;

            var weight = _prototypes.Index(id).RadiationWeight;
            if (weight <= 0 || !float.IsFinite(weight) || !CanActivate(ent.Comp.Blocks, id, i))
                continue;

            // Weighted reservoir sampling selects one candidate without allocating a list.
            totalWeight += weight;
            if (_random.NextDouble() * totalWeight < weight)
                selected = i;
        }
        if (selected < 0)
            return false;

        var value = 0;
        var threshold = round.Thresholds[selected];
        for (var shift = 0; shift < 12; shift += 4)
            value |= _random.Next((threshold >> shift) & 0xF, 16) << shift;

        ent.Comp.NextRadiationMutation = _timing.CurTime + round.RadiationMutationCooldown;
        ent.Comp.Blocks[selected] = (ushort) value;
        ent.Comp.Revision++;
        Reconcile(ent);
        _admin.Add(LogType.Action, LogImpact.High,
            $"Radiation activated genetic mutation {round.Mutations[selected]} in block {selected + 1} ({value:X3}) on {ToPrettyString(ent):target}");
        return true;
    }
}
