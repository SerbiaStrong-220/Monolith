using Content.Server.AlertLevel;
using Content.Server.StationEvents.Components;
using Content.Shared.GameTicking.Components;

namespace Content.Server.StationEvents.Events;

public sealed partial class AlertLevelInterceptionRule : StationEventSystem<AlertLevelInterceptionRuleComponent>
{
    [Dependency] private AlertLevelSystem _alertLevelSystem = default!;

    protected override void Started(EntityUid uid, AlertLevelInterceptionRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args) // Goobstation - Changed an indent.
    {
        base.Started(uid, component, gameRule, args);

        // Exodus-begin alert-level-interception-target-station
        EntityUid? chosenStation = component.TargetStation;
        if (chosenStation == null || Deleted(chosenStation.Value))
        {
            if (!TryGetRandomStation(out chosenStation))
                return;

            component.TargetStation = chosenStation.Value;
        }
        // Exodus-end

        // Exodus-begin alert-level-interception-announcement-sender
        string? announcementSender = null;
        if (TryComp<StationEventComponent>(uid, out var stationEvent) &&
            stationEvent.AnnounceSender is { } sender)
        {
            announcementSender = Loc.GetString(sender);
        }
        // Exodus-end

        // Frontier - note: levels are globally set/gotten, regardless of arg
        // Exodus-begin
        if (!component.OverrideAlert && _alertLevelSystem.GetLevel(chosenStation.Value) != "green")
            return;
        // Exodus-end

        // Exodus-begin alert-level-interception-announcement-sender
        _alertLevelSystem.SetLevel(chosenStation.Value, component.AlertLevel, true, true, true, component.Locked,
            announcementSender: announcementSender); // Goobstation
        // Exodus-end
    }
}
