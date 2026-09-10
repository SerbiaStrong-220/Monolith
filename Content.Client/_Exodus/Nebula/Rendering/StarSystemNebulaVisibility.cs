using Content.Client._Exodus.Nebula;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Nebula.Rendering;

/// <summary>
/// Keeps a shared two-second visibility transition for all Far Horizons star-system overlays.
/// Navigation entities are unaffected because only the background render passes consume it.
/// </summary>
public sealed class StarSystemNebulaVisibility
{
    private const float FadeDuration = 2f;

    private readonly NebulaSystem _nebula;
    private readonly IGameTiming _timing;

    private MapId? _mapId;
    private TimeSpan _lastUpdate;
    private uint _lastFrame;
    private float _visibility = 1f;

    public StarSystemNebulaVisibility(NebulaSystem nebula, IGameTiming timing)
    {
        _nebula = nebula;
        _timing = timing;
    }

    public float GetVisibility(in OverlayDrawArgs args)
    {
        if (_lastFrame == _timing.CurFrame)
            return _visibility;

        _lastFrame = _timing.CurFrame;

        var eye = args.Viewport.Eye;
        if (eye == null)
            return 1f;

        var mapId = eye.Position.MapId;
        var hidden = _nebula.IsInsideNebulaOrWorldEnd(mapId, eye.Position.Position);
        var now = _timing.RealTime;

        if (_mapId != mapId || now < _lastUpdate)
        {
            _mapId = mapId;
            _lastUpdate = now;
            _visibility = hidden ? 0f : 1f;
            return _visibility;
        }

        var elapsed = (float) (now - _lastUpdate).TotalSeconds;
        _lastUpdate = now;

        if (hidden)
            _visibility = MathF.Max(0f, _visibility - elapsed / FadeDuration);
        else
            _visibility = MathF.Min(1f, _visibility + elapsed / FadeDuration);

        return _visibility;
    }
}
