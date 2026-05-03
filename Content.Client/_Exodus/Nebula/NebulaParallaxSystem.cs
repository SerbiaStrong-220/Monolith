using System.Numerics;
using Content.Client.Parallax;
using Content.Client.Parallax.Data;
using Content.Client.Parallax.Managers;
using Content.Shared._Exodus.Nebula;
using Content.Shared.CCVar;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Nebula;

public sealed class NebulaParallaxSystem : EntitySystem
{
    private static readonly ProtoId<ParallaxPrototype> BlueNebulaParallax = "BlueNebula";
    private static readonly ProtoId<ParallaxPrototype> RedNebulaParallax = "RedNebula";
    private static readonly ProtoId<ParallaxPrototype> GreenNebulaParallax = "GreenNebula";
    private static readonly ProtoId<ParallaxPrototype> PurpleNebulaParallax = "PurpleNebula";
    private static readonly TimeSpan BackgroundLightningDuration = TimeSpan.FromSeconds(0.38f);
    private static readonly TimeSpan BackgroundLightningMinDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BackgroundLightningMaxDelay = TimeSpan.FromSeconds(6);
    private static readonly SoundSpecifier[] BackgroundLightningSounds =
    {
        new SoundPathSpecifier("/Audio/_Exodus/Nebula/ambient_lighting_1.ogg"),
        new SoundPathSpecifier("/Audio/_Exodus/Nebula/ambient_lighting_2.ogg"),
        new SoundPathSpecifier("/Audio/_Exodus/Nebula/ambient_lighting_3.ogg"),
        new SoundPathSpecifier("/Audio/_Exodus/Nebula/ambient_lighting_4.ogg"),
    };

    private const float TransitionSeconds = 2f;
    private const float BackgroundLightningBlendThreshold = 0.35f;
    public const int BackgroundLightningLayerIndex = 7;

    [Dependency] private readonly IParallaxManager _parallax = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;

    private ProtoId<ParallaxPrototype>? _activeParallax;
    private readonly NebulaBackgroundLightning _backgroundLightning = new();
    private float _blend;
    private TimeSpan _nextBackgroundLightning;
    private TimeSpan _backgroundLightningStart;
    private TimeSpan _backgroundLightningEnd;
    private bool _hasBackgroundLightning;

    public override void Update(float frameTime)
    {
        var targetActive = TryGetLocalPresence(out var presence) &&
                           TryGetParallaxPrototype(presence.Type, out var targetParallax);

        if (targetActive)
        {
            if (_parallax.IsLoaded(targetParallax))
                _activeParallax = targetParallax;
            else
            {
                _ = _parallax.LoadParallaxByName(targetParallax);
                targetActive = false;
            }
        }

        var targetBlend = targetActive ? 1f : 0f;
        var step = frameTime / TransitionSeconds;

        if (_blend < targetBlend)
            _blend = Math.Min(targetBlend, _blend + step);
        else if (_blend > targetBlend)
            _blend = Math.Max(targetBlend, _blend - step);

        if (_blend <= 0f && !targetActive)
            _activeParallax = null;

        UpdateBackgroundLightning();
    }

    public bool TryGetParallaxLayers(out ParallaxLayerPrepared[] layers, out float blend)
    {
        layers = Array.Empty<ParallaxLayerPrepared>();
        blend = _blend;

        if (_activeParallax is not { } parallax ||
            _blend <= 0f ||
            !_parallax.IsLoaded(parallax))
        {
            return false;
        }

        layers = _parallax.GetParallaxLayers(parallax);
        return layers.Length != 0;
    }

    public bool TryGetBackgroundLightning(out NebulaBackgroundLightning lightning, out float alpha)
    {
        lightning = _backgroundLightning;
        alpha = 0f;

        if (!_hasBackgroundLightning ||
            _timing.CurTime >= _backgroundLightningEnd)
        {
            return false;
        }

        var duration = (float) (_backgroundLightningEnd - _backgroundLightningStart).TotalSeconds;
        if (duration <= 0f)
            return false;

        var life = (float) ((_timing.CurTime - _backgroundLightningStart).TotalSeconds / duration);
        var flicker = life < 0.18f
            ? 1f
            : life < 0.36f
                ? 0.35f
                : 1f - life;

        alpha = Math.Clamp(flicker * _blend, 0f, 1f);
        return alpha > 0.03f;
    }

    private void UpdateBackgroundLightning()
    {
        if (_hasBackgroundLightning && _timing.CurTime >= _backgroundLightningEnd)
            _hasBackgroundLightning = false;

        if (!_configuration.GetCVar(CCVars.ParallaxEnabled))
            return;

        if (_nextBackgroundLightning == TimeSpan.Zero)
            ScheduleNextBackgroundLightning();

        if (_activeParallax != RedNebulaParallax ||
            _blend < BackgroundLightningBlendThreshold ||
            _timing.CurTime < _nextBackgroundLightning ||
            _hasBackgroundLightning)
        {
            return;
        }

        GenerateBackgroundLightning();
        _backgroundLightningStart = _timing.CurTime;
        _backgroundLightningEnd = _timing.CurTime + BackgroundLightningDuration;
        _hasBackgroundLightning = true;

        PlayBackgroundLightningSound();
        ScheduleNextBackgroundLightning();
    }

    private void GenerateBackgroundLightning()
    {
        var pointCount = _random.Next(6, NebulaBackgroundLightning.MaxPoints + 1);
        var start = new Vector2(_random.NextFloat(0.12f, 0.88f), 1.05f);
        var end = new Vector2(Math.Clamp(start.X + _random.NextFloat(-0.26f, 0.26f), 0.08f, 0.92f), _random.NextFloat(0.16f, 0.48f));

        _backgroundLightning.PointCount = pointCount;
        for (var i = 0; i < pointCount; i++)
        {
            var t = pointCount == 1 ? 0f : i / (pointCount - 1f);
            var point = Vector2.Lerp(start, end, t);
            var jitter = MathF.Sin(t * MathF.PI) * _random.NextFloat(-0.14f, 0.14f);
            point.X = Math.Clamp(point.X + jitter, 0.04f, 0.96f);
            _backgroundLightning.Points[i] = point;
        }

        _backgroundLightning.BranchCount = 0;
        var branchTarget = _random.Next(1, NebulaBackgroundLightning.MaxBranches + 1);
        for (var i = 1; i < pointCount - 1 && _backgroundLightning.BranchCount < branchTarget; i++)
        {
            if (!_random.Prob(0.55f))
                continue;

            var origin = _backgroundLightning.Points[i];
            var direction = _random.Prob(0.5f) ? -1f : 1f;
            var branchEnd = origin + new Vector2(direction * _random.NextFloat(0.11f, 0.24f), _random.NextFloat(-0.1f, 0.1f));
            branchEnd.X = Math.Clamp(branchEnd.X, 0.02f, 0.98f);
            branchEnd.Y = Math.Clamp(branchEnd.Y, 0.08f, 0.98f);

            var branchIndex = _backgroundLightning.BranchCount * 2;
            _backgroundLightning.Branches[branchIndex] = origin;
            _backgroundLightning.Branches[branchIndex + 1] = branchEnd;
            _backgroundLightning.BranchCount++;
        }
    }

    private void PlayBackgroundLightningSound()
    {
        var sound = _random.Pick(BackgroundLightningSounds);
        var audioParams = AudioParams.Default
            .WithVolume(_random.NextFloat(-8f, -3f))
            .WithPitchScale(_random.NextFloat(0.94f, 1.06f));

        _audio.PlayGlobal(sound, Filter.Local(), false, audioParams);
    }

    private void ScheduleNextBackgroundLightning()
    {
        var delay = _random.NextFloat((float) BackgroundLightningMinDelay.TotalSeconds, (float) BackgroundLightningMaxDelay.TotalSeconds);
        _nextBackgroundLightning = _timing.CurTime + TimeSpan.FromSeconds(delay);
    }

    private bool TryGetLocalPresence(out NebulaPresenceComponent presence)
    {
        presence = default!;

        if (_player.LocalEntity is not { Valid: true } player)
            return false;

        if (!TryComp<NebulaPresenceComponent>(player, out var playerPresence))
            return false;

        presence = playerPresence;
        return true;
    }

    private static bool TryGetParallaxPrototype(NebulaType type, out ProtoId<ParallaxPrototype> parallax)
    {
        switch (type)
        {
            case NebulaType.Blue:
                parallax = BlueNebulaParallax;
                return true;
            case NebulaType.Red:
                parallax = RedNebulaParallax;
                return true;
            case NebulaType.Green:
                parallax = GreenNebulaParallax;
                return true;
            case NebulaType.Purple:
                parallax = PurpleNebulaParallax;
                return true;
            default:
                parallax = default;
                return false;
        }
    }
}

public sealed class NebulaBackgroundLightning
{
    public const int MaxPoints = 7;
    public const int MaxBranches = 3;

    public readonly Vector2[] Points = new Vector2[MaxPoints];
    public readonly Vector2[] Branches = new Vector2[MaxBranches * 2];
    public int PointCount;
    public int BranchCount;
}
