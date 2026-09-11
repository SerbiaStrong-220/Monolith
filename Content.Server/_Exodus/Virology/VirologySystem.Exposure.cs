using Content.Shared._Exodus.Virology;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    /// <summary>Checks immunity and existing symptoms without rolling transmission or mutating the host.</summary>
    public bool CanAcquireVirus(EntityUid host, VirusDescriptor descriptor)
    {
        if (TerminatingOrDeleted(host) || !HasComp<VirusSusceptibleComponent>(host) || IsImmune(host)
            || descriptor.Symptoms.Count == 0)
            return false;

        if (TryComp<VirusImmunitiesComponent>(host, out var immunity)
            && immunity.Strains.Contains(GetIdentity(descriptor)))
            return false;

        // A merged strain already containing every incoming symptom is not a fresh target.
        foreach (var incoming in descriptor.Symptoms)
        {
            var present = false;
            foreach (var carried in EnumerateStrains(host))
            {
                if (carried.Comp.SymptomStates.ContainsKey(incoming.Symptom))
                {
                    present = true;
                    break;
                }
            }

            if (!present)
                return true;
        }

        return false;
    }

    public bool TryExpose(EntityUid target, VirusDescriptor descriptor, VirusTransmissionVector vector, float chance)
    {
        return CanAcquireVirus(target, descriptor)
               && _random.Prob(Math.Clamp(chance, 0f, 1f))
               && !IsVectorBlocked(target, vector, inhaling: vector == VirusTransmissionVector.Proximity)
               && AddVirus(target, descriptor);
    }
}
