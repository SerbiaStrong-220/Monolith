using Content.Server.Chat.Systems;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotIntelligentSystem
{
    [Dependency] private ISharedPlayerManager _players = default!;

    private void InitializeChat()
    {
        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandChatRecipients);
    }

    /// <summary>
    /// The rooted core listens and speaks through its remote eye, limited to the tiles its colony currently sees.
    /// </summary>
    private void OnExpandChatRecipients(ExpandICChatRecipientsEvent ev)
    {
        var query = EntityQueryEnumerator<RotIntelligentComponent, RotColonyStateComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out var core, out var state, out var actor))
        {
            if (!core.Alive || HasComp<PilotComponent>(uid) || core.Eye is not { } eye || TerminatingOrDeleted(eye))
                continue;
            var listening = _transform.GetMapCoordinates(eye);
            if (ev.Source == uid)
            {
                AddEyeListeners(uid, state, listening, ev);
                continue;
            }
            var source = _transform.GetMapCoordinates(ev.Source);
            if (TryGetEyeDistance(state, listening, source, ev.VoiceRange, out var distance))
                ev.Recipients.TryAdd(actor.PlayerSession, new ChatSystem.ICChatRecipientData(distance, false));
        }
    }

    private void AddEyeListeners(EntityUid core, RotColonyStateComponent state, MapCoordinates listening,
        ExpandICChatRecipientsEvent ev)
    {
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } listener || listener == core || ev.Recipients.ContainsKey(session))
                continue;
            if (TryGetEyeDistance(state, listening, _transform.GetMapCoordinates(listener), ev.VoiceRange, out var distance))
                ev.Recipients.Add(session, new ChatSystem.ICChatRecipientData(distance, false));
        }
    }

    private bool TryGetEyeDistance(RotColonyStateComponent state, MapCoordinates eye, MapCoordinates target, float range,
        out float distance)
    {
        distance = 0;
        if (eye.MapId != target.MapId)
            return false;
        distance = (target.Position - eye.Position).Length();
        return distance <= range && IsInColonyView(state, target);
    }

    /// <summary>Whether the point lies on a tile of the last private view sent to the colony's player.</summary>
    private bool IsInColonyView(RotColonyStateComponent state, MapCoordinates point)
    {
        if (state.ViewFrame is not { } frame || TerminatingOrDeleted(frame))
            return false;
        if (_mapGridQuery.TryComp(frame, out var grid))
        {
            var local = _transform.ToCoordinates(frame, point);
            return state.LastView.Contains(_maps.TileIndicesFor(frame, grid, local));
        }
        var tile = new Vector2i((int)MathF.Floor(point.Position.X), (int)MathF.Floor(point.Position.Y));
        return state.LastView.Contains(tile);
    }
}
