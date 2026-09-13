using Content.Shared._Exodus.Territory;
using Content.Shared.GameTicking;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Territory;

/// <summary>Shared countdown presentation for navigation and FTL maps.</summary>
public sealed class TerritoryCaptureDisplaySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<TerritoryCaptureComponent> _captureQuery;
    private readonly Dictionary<int, string> _countdownLabels = new();

    public override void Initialize()
    {
        base.Initialize();
        _captureQuery = GetEntityQuery<TerritoryCaptureComponent>();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public bool TryGetCapture(EntityUid grid, out TimeSpan endsAt, out Color color)
    {
        endsAt = default;
        color = default;
        if (!_captureQuery.TryGetComponent(grid, out var capture) || capture.Faction == null)
            return false;

        endsAt = capture.EndsAt;
        color = capture.Color;
        return true;
    }

    public string GetCountdown(TimeSpan endsAt)
    {
        var seconds = (int) Math.Clamp(Math.Ceiling((endsAt - _timing.CurTime).TotalSeconds), 0d, int.MaxValue);
        if (_countdownLabels.TryGetValue(seconds, out var text))
            return text;

        text = Loc.GetString("territory-contested-countdown", ("minutes", seconds / 60), ("seconds", (seconds % 60).ToString("D2")));
        _countdownLabels.Add(seconds, text);
        return text;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _countdownLabels.Clear();
    }
}
