using Content.Shared._Exodus.Virology;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    private void ReconcileSymptoms(Entity<VirusHolderComponent> ent)
    {
        var desired = new ComponentRegistry();
        var priorities = new Dictionary<string, (int Stage, string Symptom)>();
        var species = GetSpecies(ent.Owner);
        foreach (var virus in EnumerateStrains(ent.Comp))
        {
            if (virus.Comp.SuppressedUntil != null)
                continue;

            foreach (var (id, state) in virus.Comp.SymptomStates)
            {
                if (!_proto.TryIndex(id, out var symptom))
                    continue;

                foreach (var (name, entry) in BuildStageComponents(symptom, state.Stage, species))
                {
                    // Two strains can grant the same component. The strongest stage wins, with a stable tie break.
                    if (priorities.TryGetValue(name, out var priority)
                        && (priority.Stage > state.Stage
                            || (priority.Stage == state.Stage && string.CompareOrdinal(priority.Symptom, id.Id) <= 0)))
                        continue;

                    desired[name] = entry;
                    priorities[name] = (state.Stage, id.Id);
                }
            }
        }

        var retained = new Dictionary<string, GrantedVirusComponent>();
        foreach (var (name, granted) in ent.Comp.GrantedComponents)
        {
            if (granted.Instance.Deleted)
                continue;

            if (desired.TryGetValue(name, out var entry) && ReferenceEquals(entry.Component, granted.Template))
                retained.Add(name, granted);
            else
                RemComp(ent.Owner, granted.Instance);
        }

        var additions = new ComponentRegistry();
        foreach (var (name, entry) in desired)
        {
            if (!_factory.TryGetRegistration(name, out var registration)
                || HasComp(ent.Owner, registration.Type))
                continue;

            additions[name] = entry;
        }

        // Add the entire set together so startup handlers can see companion components regardless of YAML order.
        EntityManager.AddComponents(ent.Owner, additions);
        foreach (var (name, entry) in additions)
        {
            var registration = _factory.GetRegistration(name);
            if (EntityManager.TryGetComponent(ent.Owner, registration.Type, out var component))
                retained[name] = new GrantedVirusComponent(component, entry.Component);
        }

        ent.Comp.GrantedComponents = retained;
    }
}
