using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class VirusEpidemicRuleSystem : GameRuleSystem<VirusEpidemicRuleComponent>
{
    [Dependency] private VirologySystem _virology = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    protected override void Started(EntityUid uid, VirusEpidemicRuleComponent component,
        GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        component.SeedAt = Timing.CurTime + component.Preparation;
        component.NextCheck = Timing.CurTime;
        ChatManager.DispatchServerAnnouncement(Loc.GetString(component.PreparationMessage));
    }

    protected override void ActiveTick(EntityUid uid, VirusEpidemicRuleComponent component,
        GameRuleComponent gameRule, float frameTime)
    {
        var now = Timing.CurTime;
        if (now < component.NextCheck)
            return;

        component.NextCheck = now + TimeSpan.FromSeconds(30);
        if (!component.Seeded)
        {
            if (now < component.SeedAt)
                return;

            Seed((uid, component));
        }

        var counts = CountThreats(component.Symptom);
        var stage = counts.Reservoirs > 0 ? 3 : counts.Broods + counts.Offspring > 0 ? 2 : counts.Carriers > 0 ? 1 : 0;
        if (stage > component.WarningStage)
        {
            component.WarningStage = stage;
            if (stage <= component.WarningMessages.Length)
                ChatManager.DispatchServerAnnouncement(Loc.GetString(component.WarningMessages[stage - 1]));
        }

        if (stage > 0)
        {
            if (component.Controlled)
                ChatManager.DispatchServerAnnouncement(Loc.GetString(component.ResurgenceMessage));
            component.Controlled = false;
            component.QuietSince = null;
        }
        else
        {
            component.QuietSince ??= now;
            if (!component.Controlled && now - component.QuietSince >= component.QuietPeriod)
            {
                component.Controlled = true;
                ChatManager.DispatchServerAnnouncement(Loc.GetString(component.ControlledMessage));
            }
        }
    }

    /// <summary>Seed once, preferring different grids without overriding immunity or player roles.</summary>
    public void Seed(Entity<VirusEpidemicRuleComponent> ent)
    {
        if (ent.Comp.Seeded)
            return;

        ent.Comp.Seeded = true;
        if (_virology.BuildDescriptor(ent.Comp.Virus) is not { } descriptor)
            return;

        var candidates = new List<EntityUid>();
        var query = EntityQueryEnumerator<VirusSusceptibleComponent, ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.MapUid != null && _mobState.IsAlive(uid) && _virology.CanAcquireVirus(uid, descriptor))
                candidates.Add(uid);
        }

        var wanted = Math.Min(candidates.Count,
            Math.Clamp((int)Math.Ceiling(candidates.Count / (double)Math.Max(1, ent.Comp.PlayersPerCarrier)),
                1, Math.Max(1, ent.Comp.MaxCarriers)));
        var grids = new HashSet<EntityUid?>();
        RobustRandom.Shuffle(candidates);
        for (var pass = 0; pass < 2 && ent.Comp.SeededCount < wanted; pass++)
        {
            foreach (var candidate in candidates)
            {
                var grid = Transform(candidate).GridUid;
                if (pass == 0 && grids.Contains(grid))
                    continue;
                if (!_virology.AddVirus(candidate, descriptor))
                    continue;

                grids.Add(grid);
                ent.Comp.SeededCount++;
                if (ent.Comp.SeededCount >= wanted)
                    break;
            }
        }

        Log.Info($"Epidemic seeded {ent.Comp.SeededCount} carriers of {ent.Comp.Virus}.");
    }

    public (int Carriers, int Broods, int Offspring, int Reservoirs) CountThreats(ProtoId<VirusSymptomPrototype> symptom)
    {
        var carriers = 0;
        var broods = 0;
        var offspring = 0;
        var reservoirs = 0;
        var hosts = EntityQueryEnumerator<VirusHolderComponent>();
        while (hosts.MoveNext(out var uid, out var holder))
        {
            // Incubating bodies have their own counter; other contagious corpses still matter.
            if (_mobState.IsDead(uid) && HasComp<VirusBroodComponent>(uid))
                continue;
            foreach (var strain in _virology.EnumerateStrains(holder))
            {
                if (strain.Comp.SuppressedUntil != null || !strain.Comp.SymptomStates.ContainsKey(symptom))
                    continue;
                carriers++;
                break;
            }
        }

        var bodies = EntityQueryEnumerator<VirusBroodComponent>();
        while (bodies.MoveNext(out _, out var brood))
        {
            if (brood.Symptom == symptom && brood.Incubating && !brood.Hatched)
                broods++;
        }

        var children = EntityQueryEnumerator<VirusOffspringComponent>();
        while (children.MoveNext(out _, out var child))
        {
            if (!child.Finished && ContainsSymptom(child.Strain, symptom))
                offspring++;
        }

        var sources = EntityQueryEnumerator<VirusReservoirComponent>();
        while (sources.MoveNext(out _, out var source))
        {
            if (ContainsSymptom(source.Strain, symptom))
                reservoirs++;
        }

        return (carriers, broods, offspring, reservoirs);
    }

    private static bool ContainsSymptom(VirusDescriptor? descriptor, ProtoId<VirusSymptomPrototype> symptom)
    {
        if (descriptor == null)
            return false;
        foreach (var snapshot in descriptor.Symptoms)
        {
            if (snapshot.Symptom == symptom)
                return true;
        }

        return false;
    }

    protected override void AppendRoundEndText(EntityUid uid, VirusEpidemicRuleComponent component,
        GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        var counts = CountThreats(component.Symptom);
        args.AddLine(Loc.GetString(component.RoundEndMessage, ("seeded", component.SeededCount),
            ("carriers", counts.Carriers), ("broods", counts.Broods),
            ("offspring", counts.Offspring), ("reservoirs", counts.Reservoirs)));
    }
}
