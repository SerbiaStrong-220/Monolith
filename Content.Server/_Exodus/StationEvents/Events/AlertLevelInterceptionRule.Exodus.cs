using Content.Server.StationEvents.Components;

namespace Content.Server.StationEvents.Events;

public sealed partial class AlertLevelInterceptionRule
{
    public bool TrySetTargetStation(EntityUid rule, EntityUid station)
    {
        if (TerminatingOrDeleted(station) ||
            !TryComp<AlertLevelInterceptionRuleComponent>(rule, out var alertRule))
        {
            return false;
        }

        alertRule.TargetStation = station;
        return true;
    }
}
