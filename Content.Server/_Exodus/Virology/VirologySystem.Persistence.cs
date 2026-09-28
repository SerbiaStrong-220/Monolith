using Content.Shared._Exodus.Virology;
using Robust.Shared.Map.Events;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    private void InitializePersistence()
    {
        SubscribeLocalEvent<BeforeSerializationEvent>(OnBeforeSave);
        SubscribeLocalEvent<AfterSerializationEvent>(OnAfterSave);
        SubscribeLocalEvent<VirusHolderComponent, ComponentStartup>(OnHolderStartup);
    }

    private void OnBeforeSave(BeforeSerializationEvent args)
    {
        // Saving is infrequent; do not copy every host's strain and symptom dictionaries every game tick.
        var query = AllEntityQuery<VirusHolderComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var holder, out var transform))
        {
            if (TerminatingOrDeleted(uid) || !args.MapIds.Contains(transform.MapID))
                continue;
            var root = uid;
            while (!args.Entities.Contains(root) && TryComp(root, out TransformComponent? parent))
                root = parent.ParentUid;
            if (!args.Entities.Contains(root))
                continue;

            holder.SavedViruses.Clear();
            holder.SavedGrantedComponents.Clear();
            foreach (var virus in EnumerateStrains(holder))
            {
                var descriptor = ToDescriptor(virus);
                // Save the actual rolled cure and runtime settings, even for an unmutated prototype strain.
                descriptor.Name = virus.Comp.Name;
                descriptor.Cure = virus.Comp.Cure?.Clone();
                descriptor.Transmission = virus.Comp.Transmission?.Clone();
                descriptor.IsSupervirus = virus.Comp.IsSupervirus;
                var saved = new VirusSavedState
                {
                    Strain = descriptor,
                    IncubationEndsAt = virus.Comp.IncubationEndsAt,
                    HiddenUntil = virus.Comp.HiddenUntil,
                    SuppressedUntil = virus.Comp.SuppressedUntil,
                    NextEffect = virus.Comp.NextEffect,
                };
                foreach (var (id, state) in virus.Comp.SymptomStates)
                {
                    saved.Symptoms.Add(id, new VirusSymptomState
                    {
                        Stage = state.Stage,
                        StageStartTime = state.StageStartTime,
                        Accelerant = state.Accelerant,
                        Revealed = state.Revealed,
                        LastEmote = state.LastEmote,
                        EmoteDelay = state.EmoteDelay,
                    });
                }
                holder.SavedViruses.Add(saved);
            }
            foreach (var (name, granted) in holder.GrantedComponents)
            {
                if (!granted.Instance.Deleted && _factory.TryGetRegistration(name, out var registration)
                    && EntityManager.TryGetComponent(uid, registration.Type, out var current)
                    && ReferenceEquals(current, granted.Instance))
                    holder.SavedGrantedComponents.Add(name);
            }
        }
    }

    private void OnAfterSave(AfterSerializationEvent args)
    {
        var query = AllEntityQuery<VirusHolderComponent>();
        while (query.MoveNext(out _, out var holder))
        {
            holder.SavedViruses.Clear();
            holder.SavedGrantedComponents.Clear();
        }
    }

    private void OnHolderStartup(Entity<VirusHolderComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.SavedViruses.Count == 0)
            return;
        foreach (var saved in ent.Comp.SavedViruses)
        {
            // Loading is not infection: do not merge strains, roll new cures or reset incubation/stages.
            var uid = Spawn(BaseVirusProto);
            var virus = Comp<VirusComponent>(uid);
            var strain = saved.Strain;
            virus.Carrier = ent;
            virus.Source = strain.Source;
            virus.Name = strain.Name;
            virus.Genome = strain.Genome;
            virus.Cure = strain.Cure;
            virus.Transmission = strain.Transmission;
            virus.Incubation = strain.Incubation;
            virus.SymptomTimeMultiplier = strain.SymptomTimeMultiplier;
            virus.IsSupervirus = strain.IsSupervirus;
            virus.SymptomStates = saved.Symptoms;
            virus.IncubationEndsAt = saved.IncubationEndsAt;
            virus.HiddenUntil = saved.HiddenUntil;
            virus.SuppressedUntil = saved.SuppressedUntil;
            virus.NextEffect = saved.NextEffect;
            ent.Comp.Viruses.Add(uid);
        }
        ent.Comp.SavedViruses.Clear();
        ReconcileSymptoms(ent, restoreOwnership: true);
    }
}
