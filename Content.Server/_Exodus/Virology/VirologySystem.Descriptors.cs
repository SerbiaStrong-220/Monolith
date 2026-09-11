// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared._Exodus.Virology;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    public VirusDescriptor? BuildDescriptor(EntProtoId protoId)
    {
        if (!_proto.Resolve(protoId, out var proto)
            || !proto.TryGetComponent<VirusComponent>(out var config, _factory))
            return null;

        var descriptor = new VirusDescriptor
        {
            Source = protoId,
            Genome = GetGenome(config.Symptoms),
        };

        var used = new HashSet<ProtoId<ReagentPrototype>>();
        foreach (var symptom in config.Symptoms)
        {
            var accelerant = RollAccelerant(used);
            if (accelerant is { } picked)
                used.Add(picked);

            descriptor.Symptoms.Add(new VirusSymptomSnapshot { Symptom = symptom, Accelerant = accelerant });
        }

        return descriptor;
    }

    private VirusComponent? GetStrainConfig(EntProtoId proto)
    {
        return _proto.Resolve(proto, out var entProto) && entProto.TryGetComponent<VirusComponent>(out var config, _factory)
            ? config
            : null;
    }

    public string? ResolveName(VirusDescriptor descriptor)
    {
        return descriptor.Source is { } source && GetStrainConfig(source) is { NameLoc: { } loc }
            ? Loc.GetString(loc)
            : descriptor.Name;
    }

    public VirusTransmission? ResolveTransmission(VirusDescriptor descriptor)
    {
        return descriptor.Source is { } source && GetStrainConfig(source) is { } config
            ? config.Transmission
            : descriptor.Transmission;
    }

    public VirusCure? ResolveCure(VirusDescriptor descriptor)
    {
        return descriptor.Source is { } source ? GetRoundCure(source) : descriptor.Cure;
    }

    public VirusDescriptor ToDescriptor(Entity<VirusComponent> virus)
    {
        var descriptor = new VirusDescriptor
        {
            Source = virus.Comp.Source,
            Genome = virus.Comp.Genome,
            SuppressedRemaining = virus.Comp.SuppressedUntil is { } until ? until - _timing.CurTime : null,
        };

        if (virus.Comp.Source == null)
        {
            descriptor.Name = virus.Comp.Name;
            descriptor.Cure = virus.Comp.Cure?.Clone();
            descriptor.Transmission = virus.Comp.Transmission?.Clone();
            descriptor.IsSupervirus = virus.Comp.IsSupervirus;
        }

        foreach (var (symptomId, state) in virus.Comp.SymptomStates)
        {
            descriptor.Symptoms.Add(new VirusSymptomSnapshot
            {
                Symptom = symptomId,
                Stage = state.Stage,
                Revealed = state.Revealed,
                Accelerant = state.Accelerant,
            });
        }

        return descriptor;
    }

    public EntityUid? SpawnVirus(EntityUid host, VirusDescriptor descriptor)
    {
        if (Terminating(host) || descriptor.Symptoms.Count == 0)
            return null;

        var holder = EnsureHolder(host);

        var virus = Spawn(descriptor.Source?.Id ?? BaseVirusProto);
        var comp = EnsureComp<VirusComponent>(virus);
        comp.Carrier = host;
        comp.Source = descriptor.Source;
        comp.Genome = descriptor.Genome;

        if (descriptor.Source is { } source)
        {
            comp.Name = comp.NameLoc is { } loc ? Loc.GetString(loc) : null;
            comp.Cure = GetRoundCure(source)?.Clone();
        }
        else
        {
            comp.Name = descriptor.Name;
            comp.Cure = descriptor.Cure?.Clone();
            comp.Transmission = descriptor.Transmission?.Clone();
            comp.IsSupervirus = descriptor.IsSupervirus;
        }

        foreach (var snapshot in descriptor.Symptoms)
        {
            comp.SymptomStates[snapshot.Symptom] = new VirusSymptomState
            {
                Stage = GetInitialStage(snapshot),
                StageStartTime = _timing.CurTime,
                LastEmote = _timing.CurTime,
                Revealed = snapshot.Revealed,
                Accelerant = snapshot.Accelerant,
            };
        }

        // if infected with suppressed strain - spawns suppressed and keeps timer
        if (descriptor.SuppressedRemaining is { } remaining && remaining > TimeSpan.Zero)
            comp.SuppressedUntil = _timing.CurTime + remaining;

        // virus kept in nullspace (server-only, never networked)
        holder.Viruses.Add(virus);

        RefreshSymptoms((virus, comp));

        RaiseContentsChanged(host);

        _adminLog.Add(LogType.Virology, LogImpact.Medium,
            $"{ToPrettyString(host):target} was infected with virus {DescribeVirus(comp)}");

        return virus;
    }

    // for admin logs
    public static string DescribeVirus(VirusComponent comp)
    {
        var name = comp.Name ?? comp.Source?.Id ?? "mutant";
        var symptoms = string.Join(", ", comp.SymptomStates.Keys);
        return $"'{name}' [{comp.Genome}{(comp.IsSupervirus ? ", supervirus" : "")}] symptoms: {symptoms}";
    }

    public void RemoveVirus(Entity<VirusComponent> virus)
    {
        if (virus.Comp.Removing || Terminating(virus.Owner))
            return;

        virus.Comp.Removing = true;
        var carrier = virus.Comp.Carrier;
        if (!Terminating(carrier) && TryComp<VirusHolderComponent>(carrier, out var holder))
        {
            holder.Viruses.Remove(virus.Owner);
            ReconcileSymptoms((carrier, holder));
            RaiseContentsChanged(carrier);
        }

        QueueDel(virus.Owner);
    }

    public void RaiseContentsChanged(EntityUid carrier)
    {
        var ev = new VirusContentsChangedEvent();
        RaiseLocalEvent(carrier, ref ev);
    }

    private static readonly HashSet<EntityUid> EmptyStrains = [];

    public StrainQuery EnumerateStrains(EntityUid host)
        => new(EntityManager, TryComp<VirusHolderComponent>(host, out var holder) ? holder.Viruses : EmptyStrains);

    public StrainQuery EnumerateStrains(VirusHolderComponent holder) => new(EntityManager, holder.Viruses);

    public List<Entity<VirusComponent>> GetStrains(EntityUid host)
    {
        var list = new List<Entity<VirusComponent>>();
        foreach (var strain in EnumerateStrains(host))
            list.Add(strain);

        return list;
    }

    public readonly struct StrainQuery(IEntityManager entMan, HashSet<EntityUid> viruses)
    {
        public Enumerator GetEnumerator() => new(entMan, viruses);

        public struct Enumerator(IEntityManager entMan, HashSet<EntityUid> viruses)
        {
            private HashSet<EntityUid>.Enumerator _inner = viruses.GetEnumerator();

            public Entity<VirusComponent> Current { get; private set; }

            public bool MoveNext()
            {
                while (_inner.MoveNext())
                {
                    if (entMan.TryGetComponent<VirusComponent>(_inner.Current, out var comp) && !comp.Removing)
                    {
                        Current = (_inner.Current, comp);
                        return true;
                    }
                }

                return false;
            }
        }
    }

    private VirusHolderComponent EnsureHolder(EntityUid host) => EnsureComp<VirusHolderComponent>(host);

    private int GetInitialStage(VirusSymptomSnapshot snapshot)
    {
        if (!_proto.TryIndex(snapshot.Symptom, out var symptom) || symptom.ResetProgressOnInfection)
            return 0;

        return Math.Clamp(snapshot.Stage, 0, Math.Max(0, symptom.Stages.Length - 1));
    }
}
