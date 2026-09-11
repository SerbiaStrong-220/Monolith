// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared._Exodus.Virology;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirusOutbreakRuleSystem : StationEventSystem<VirusOutbreakRuleComponent>
{
    [Dependency] private VirologySystem _virology = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    protected override void Started(EntityUid uid, VirusOutbreakRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var viruses = new List<EntProtoId>();
        foreach (var proto in PrototypeManager.EnumeratePrototypes<EntityPrototype>())
        {
            if (!proto.Abstract && proto.TryGetComponent<VirusComponent>(out var virus, _factory)
                && virus.RandomOutbreakEligible && virus.Symptoms.Count > 0)
                viruses.Add(proto.ID);
        }

        if (viruses.Count == 0)
            return;

        var candidates = new List<EntityUid>();
        var query = EntityQueryEnumerator<VirusSusceptibleComponent, MindContainerComponent, HumanoidAppearanceComponent>();
        while (query.MoveNext(out var ent, out _, out var mind, out _))
        {
            if (mind.HasMind && _mobState.IsAlive(ent) && !_virology.IsImmune(ent))
                candidates.Add(ent);
        }

        if (candidates.Count == 0)
            return;

        var max = Math.Clamp(component.MaxVictims, 0, candidates.Count);
        var min = Math.Clamp(component.MinVictims, 0, max);
        var victims = RobustRandom.Next(min, max + 1);
        for (var i = 0; i < victims; i++)
            _virology.AddVirus(RobustRandom.PickAndTake(candidates), RobustRandom.Pick(viruses));
    }
}
