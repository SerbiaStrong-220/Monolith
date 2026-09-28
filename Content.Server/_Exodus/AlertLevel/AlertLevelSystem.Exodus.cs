using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.AlertLevel;

public sealed partial class AlertLevelSystem
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly SoundSpecifier SectorTransitionSound =
        new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    /// <summary>
    /// Starts the first half of a sector-code change. The previous code remains active.
    /// </summary>
    private void BeginSectorCodeTransition(
        Entity<AlertLevelComponent> sector,
        string level,
        AlertLevelDetail detail,
        bool playSound,
        bool announce,
        bool locked)
    {
        var component = sector.Comp;
        component.PendingLevel = level;
        component.PendingAt = _timing.CurTime + detail.TransitionDuration;
        component.PendingPlaySound = playSound;
        component.PendingAnnounce = announce;
        component.PendingLocked = locked;

        if (playSound)
        {
            var filter = Filter.Empty();
            filter.AddInMap(_ticker.DefaultMap, EntityManager);
            _audio.PlayGlobal(SectorTransitionSound, filter, true, SectorTransitionSound.Params);
        }

        if (announce && detail.StartAnnouncement is { } startId && Loc.TryGetString(startId, out var start))
        {
            _chatSystem.DispatchGlobalAnnouncement(
                start,
                sender: Loc.GetString("faction-alert-announcement-sender"),
                playSound: false,
                colorOverride: detail.Color);
        }

        var ev = new SectorCodeTransitionChangedEvent();
        RaiseLocalEvent(ref ev);
    }

    /// <summary>
    /// Applies the pending sector code once its <see cref="AlertLevelComponent.PendingAt"/> is reached.
    /// </summary>
    private void TryCompleteSectorCodeTransition(Entity<AlertLevelComponent> station)
    {
        var alert = station.Comp;
        if (alert.PendingLevel is not { } pending || _timing.CurTime < alert.PendingAt)
            return;

        alert.PendingLevel = null;
        alert.ActiveDelay = false;
        alert.CurrentDelay = 0;
        SetLevel(station, pending, alert.PendingPlaySound, alert.PendingAnnounce, true, alert.PendingLocked);
        var ev = new SectorCodeTransitionChangedEvent();
        RaiseLocalEvent(ref ev);
    }
}

[ByRefEvent]
public readonly record struct SectorCodeTransitionChangedEvent;
