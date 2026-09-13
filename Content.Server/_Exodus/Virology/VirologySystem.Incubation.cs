using Content.Shared._Exodus.Virology;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    public bool HasDetectableVirus(EntityUid host)
    {
        foreach (var strain in EnumerateStrains(host))
        {
            if (strain.Comp.HiddenUntil is { } hiddenUntil && _timing.CurTime < hiddenUntil)
                continue;

            foreach (var (id, state) in strain.Comp.SymptomStates)
            {
                if (_proto.Resolve(id, out var symptom)
                    && state.Stage >= 0 && state.Stage < symptom.Stages.Length
                    && !symptom.Stages[state.Stage].HiddenOnHealthAnalyzer)
                    return true;
            }
        }

        return false;
    }

    private void InitializeIncubation(VirusComponent virus)
    {
        if (!float.IsFinite(virus.SymptomTimeMultiplier) || virus.SymptomTimeMultiplier <= 0f)
        {
            Log.Error($"Virus {virus.Source} has an invalid symptomTimeMultiplier; using 1.");
            virus.SymptomTimeMultiplier = 1f;
        }

        virus.NextEffect = _timing.CurTime;
        if (virus.Incubation is not { } incubation)
            return;

        var hidden = RollIncubation(incubation.Hidden, incubation.HiddenMax);
        var visible = RollIncubation(incubation.Visible, incubation.VisibleMax);
        if (hidden > TimeSpan.Zero)
            virus.HiddenUntil = _timing.CurTime + hidden;
        if (hidden + visible > TimeSpan.Zero)
            virus.IncubationEndsAt = _timing.CurTime + hidden + visible;
    }

    private TimeSpan RollIncubation(TimeSpan minimum, TimeSpan? maximum)
    {
        minimum = minimum < TimeSpan.Zero ? TimeSpan.Zero : minimum;
        return maximum is { } upper && upper > minimum ? _random.Next(minimum, upper) : minimum;
    }

    private static void DelayIncubation(VirusComponent virus, TimeSpan delay)
    {
        virus.IncubationEndsAt += delay;
        virus.HiddenUntil += delay;
    }

    private bool TickIncubation(Entity<VirusComponent> virus, TimeSpan now)
    {
        if (virus.Comp.IncubationEndsAt is not { } until)
            return false;
        if (now < until)
            return true;

        FinishIncubation(virus);
        return false;
    }

    private void FinishIncubation(Entity<VirusComponent> virus)
    {
        if (virus.Comp.IncubationEndsAt == null)
            return;

        virus.Comp.IncubationEndsAt = null;
        virus.Comp.HiddenUntil = null;
        virus.Comp.NextEffect = _timing.CurTime;
        foreach (var state in virus.Comp.SymptomStates.Values)
        {
            state.StageStartTime = _timing.CurTime;
            state.LastEmote = _timing.CurTime;
            state.EmoteDelay = TimeSpan.Zero;
        }

        RefreshSymptoms(virus);
        RaiseContentsChanged(virus.Comp.Carrier);
    }
}
