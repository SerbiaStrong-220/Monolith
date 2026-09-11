// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Server.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Implants.Components;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Virology;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private EntityQuery<VirusImmunityComponent> _immunityQuery;
    private EntityQuery<ImplantedComponent> _implantedQuery;

    private void InitializeImmunity()
    {
        _immunityQuery = GetEntityQuery<VirusImmunityComponent>();
        _implantedQuery = GetEntityQuery<ImplantedComponent>();
        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(OnJobsAssigned);
    }

    public bool IsImmune(EntityUid host)
    {
        if (_immunityQuery.HasComponent(host))
            return true;

        if (!_implantedQuery.TryGetComponent(host, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (_immunityQuery.HasComponent(implant))
                return true;
        }

        return false;
    }

    private void OnJobsAssigned(RulePlayerJobsAssignedEvent ev)
    {
        var min = _cfg.GetCVar(EXCVars.VirologyImmuneCountMin);
        var max = _cfg.GetCVar(EXCVars.VirologyImmuneCountMax);
        if (max <= 0)
            return;

        var eligible = new List<EntityUid>();
        foreach (var player in ev.Players)
        {
            if (player.AttachedEntity is { } mob
                && HasComp<HumanoidAppearanceComponent>(mob)
                && HasComp<VirusSusceptibleComponent>(mob)
                && !IsImmune(mob))
                eligible.Add(mob);
        }

        max = Math.Min(max, eligible.Count);
        min = Math.Clamp(min, 0, max);
        var count = _random.Next(min, max + 1);
        for (var i = 0; i < count; i++)
            AddComp<VirusImmunityComponent>(_random.PickAndTake(eligible));
    }
}
