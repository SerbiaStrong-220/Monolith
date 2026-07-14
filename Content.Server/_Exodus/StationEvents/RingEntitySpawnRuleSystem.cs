using Content.Server._Exodus.StationEvents.Components;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Exodus.StationEvents;

/// <summary>
/// Handles game rules that create a single entity in a configurable ring around the primary map origin.
/// </summary>
public sealed class RingEntitySpawnRuleSystem : StationEventSystem<RingEntitySpawnRuleComponent>
{
    [Dependency] private IRobustRandom _random = default!;

    protected override void Started(EntityUid uid, RingEntitySpawnRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.MinimumDistance < 0f || component.MaximumDistance < component.MinimumDistance)
        {
            Sawmill.Error($"Invalid spawn distance ring for {ToPrettyString(uid):rule}");
            ForceEndSelf(uid, gameRule);
            return;
        }

        if (!MapSystem.TryGetMap(GameTicker.DefaultMap, out _))
        {
            Sawmill.Error($"Primary map was unavailable while starting {ToPrettyString(uid):rule}");
            ForceEndSelf(uid, gameRule);
            return;
        }

        var position = _random.NextVector2(component.MinimumDistance, component.MaximumDistance);
        Spawn(component.Prototype, new MapCoordinates(position, GameTicker.DefaultMap));

        if (component.Announcement != null)
        {
            var players = Filter.Empty().AddWhere(GameTicker.UserHasJoinedGame);
            var message = Loc.GetString(component.Announcement.Value,
                ("x", (int) position.X),
                ("y", (int) position.Y));

            ChatSystem.DispatchFilteredAnnouncement(players, message,
                playSound: component.AnnouncementSound != null,
                announcementSound: component.AnnouncementSound,
                colorOverride: component.AnnouncementColor);
        }

        ForceEndSelf(uid, gameRule);
    }
}
