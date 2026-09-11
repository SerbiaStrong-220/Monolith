// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Chemistry.Reagent;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared._Exodus.Virology;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    /// <summary>Sorted symptom ids, used for immunity and re-infection checks. Cached per strain.</summary>
    public string GetIdentity(VirusComponent virus)
    {
        if (virus.CachedIdentity is { } cached)
            return cached;

        _identityBuf.Clear();
        foreach (var symptom in virus.SymptomStates.Keys)
            _identityBuf.Add(symptom.Id);

        return virus.CachedIdentity = BuildIdentity(_identityBuf);
    }

    public string GetIdentity(VirusDescriptor descriptor)
    {
        _identityBuf.Clear();
        foreach (var snapshot in descriptor.Symptoms)
            _identityBuf.Add(snapshot.Symptom.Id);

        return BuildIdentity(_identityBuf);
    }

    /// <summary>Identity of the supervirus produced by merging with the incoming descriptor.</summary>
    private string GetUnionIdentity(VirusComponent target, VirusDescriptor incoming)
    {
        _identityBuf.Clear();
        foreach (var symptom in target.SymptomStates.Keys)
            _identityBuf.Add(symptom.Id);

        foreach (var snapshot in incoming.Symptoms)
        {
            if (_identityBuf.Count >= MaxSupervirusSymptoms)
                break;

            var id = snapshot.Symptom.Id;
            if (!_identityBuf.Contains(id))
                _identityBuf.Add(id);
        }

        return BuildIdentity(_identityBuf);
    }

    private static string BuildIdentity(List<string> symptomIds)
    {
        symptomIds.Sort(StringComparer.Ordinal);
        return string.Join(',', symptomIds);
    }

    /// <summary>Strain genome is set by first symptom.</summary>
    public VirusGenome GetGenome(List<ProtoId<VirusSymptomPrototype>> symptoms)
    {
        if (symptoms.Count > 0 && _proto.Resolve(symptoms[0], out var first))
            return first.Genome;

        return VirusGenome.Rna;
    }

    public ProtoId<ReagentPrototype>? RollAccelerant(IReadOnlySet<ProtoId<ReagentPrototype>>? exclude = null)
    {
        if (!_proto.Resolve<VirusCurePoolPrototype>(AccelerantPool, out var pool) || pool.Accelerants.Count == 0)
            return null;

        if (exclude is { Count: > 0 })
        {
            _accelerantBuf.Clear();
            foreach (var accelerant in pool.Accelerants)
            {
                if (!exclude.Contains(accelerant))
                    _accelerantBuf.Add(accelerant);
            }

            if (_accelerantBuf.Count > 0)
                return _random.Pick(_accelerantBuf);
        }

        return _random.Pick(pool.Accelerants);
    }

    /// <summary>Accelerants.</summary>
    public static HashSet<ProtoId<ReagentPrototype>> CollectAccelerants(VirusComponent virus, VirusSymptomState? except = null)
    {
        var used = new HashSet<ProtoId<ReagentPrototype>>();
        foreach (var state in virus.SymptomStates.Values)
        {
            if (ReferenceEquals(state, except))
                continue;

            if (state.Accelerant is { } accelerant)
                used.Add(accelerant);
        }

        return used;
    }

    public static HashSet<ProtoId<ReagentPrototype>> CollectAccelerants(VirusDescriptor descriptor)
    {
        var used = new HashSet<ProtoId<ReagentPrototype>>();
        foreach (var snapshot in descriptor.Symptoms)
        {
            if (snapshot.Accelerant is { } accelerant)
                used.Add(accelerant);
        }

        return used;
    }

    /// <summary>Grants immunity to a strain identity. Returns true if it was newly granted.</summary>
    public bool AddImmunity(EntityUid host, string identity)
    {
        var immunities = EnsureComp<VirusImmunitiesComponent>(host);
        if (!immunities.Strains.Add(identity))
            return false;

        Dirty(host, immunities);
        return true;
    }

    public ProtoId<SpeciesPrototype>? GetSpecies(EntityUid uid)
    {
        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return humanoid.Species;

        return null;
    }
}
