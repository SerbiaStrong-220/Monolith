using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client.Parallax;
using Content.Client.Parallax.Data;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Hazards;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Nebula.Rendering;

public sealed partial class NebulaParallaxSystem : EntitySystem
{
    private static readonly TimeSpan BackgroundLightningDuration = TimeSpan.FromSeconds(0.38f);
    private static readonly TimeSpan BackgroundLightningMinDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BackgroundLightningMaxDelay = TimeSpan.FromSeconds(6);
    private static readonly SoundSpecifier BackgroundLightningSound = new SoundCollectionSpecifier("ExodusNebulaAmbientLightning");

    private const float TransitionSeconds = 2f;
    private const float BackgroundLightningBlendThreshold = 0.35f;
    private const float BackgroundLightningFlickerFlashEnd = 0.18f;
    private const float BackgroundLightningFlickerDimEnd = 0.36f;
    private const float BackgroundLightningFlickerDimAlpha = 0.35f;
    private const float BackgroundLightningVisibleAlphaThreshold = 0.03f;
    private const int BackgroundLightningMinPoints = 6;
    private const float BackgroundLightningStartMinX = 0.12f;
    private const float BackgroundLightningStartMaxX = 0.88f;
    private const float BackgroundLightningStartY = 1.05f;
    private const float BackgroundLightningEndMinY = 0.16f;
    private const float BackgroundLightningEndMaxY = 0.48f;
    private const float BackgroundLightningEndMaxXOffset = 0.26f;
    private const float BackgroundLightningEndMinX = 0.08f;
    private const float BackgroundLightningEndMaxX = 0.92f;
    private const float BackgroundLightningJitter = 0.14f;
    private const float BackgroundLightningPointMinX = 0.04f;
    private const float BackgroundLightningPointMaxX = 0.96f;
    private const float BackgroundLightningBranchChance = 0.55f;
    private const float BackgroundLightningBranchDirectionChance = 0.5f;
    private const float BackgroundLightningBranchMinLength = 0.11f;
    private const float BackgroundLightningBranchMaxLength = 0.24f;
    private const float BackgroundLightningBranchVerticalJitter = 0.1f;
    private const float BackgroundLightningBranchMinX = 0.02f;
    private const float BackgroundLightningBranchMaxX = 0.98f;
    private const float BackgroundLightningBranchMinY = 0.08f;
    private const float BackgroundLightningBranchMaxY = 0.98f;
    private const float BackgroundLightningMinVolume = -8f;
    private const float BackgroundLightningMaxVolume = -3f;
    private const float BackgroundLightningMinPitch = 0.94f;
    private const float BackgroundLightningMaxPitch = 1.06f;
    private const string ParallaxOverrideKey = "exodus-nebula";
    private const int ParallaxOverridePriority = 100;

    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private ParallaxSystem _parallax = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;

    private EntProtoId? _activeMarker;
    private ProtoId<ParallaxPrototype>? _activeParallax;
    private bool _activeMarkerHasLightning;
    private readonly NebulaBackgroundLightning _backgroundLightning = new();
    private float _blend;
    private TimeSpan _nextBackgroundLightning;
    private TimeSpan _backgroundLightningStart;
    private TimeSpan _backgroundLightningEnd;
    private bool _hasBackgroundLightning;
    private bool _parallaxEnabled;

    public override void Initialize()
    {
        base.Initialize();
        _configuration.OnValueChanged(CCVars.ParallaxEnabled, OnParallaxEnabledChanged, true);
        _overlay.AddOverlay(new NebulaLightningOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _configuration.UnsubValueChanged(CCVars.ParallaxEnabled, OnParallaxEnabledChanged);
        _parallax.ClearParallaxOverride(ParallaxOverrideKey);
        _overlay.RemoveOverlay<NebulaLightningOverlay>();
    }

    public override void Update(float frameTime)
    {
        var targetActive = false;
        if (TryGetLocalPresence(out var presence) &&
            TryGetMarkerData(presence.Marker, out var targetParallax, out var hasLightning) &&
            targetParallax is { } parallax)
        {
            if (_parallax.IsParallaxLoaded(parallax))
            {
                _activeParallax = parallax;
                _activeMarker = presence.Marker;
                _activeMarkerHasLightning = hasLightning;
                targetActive = true;
            }
            else
            {
                _parallax.LoadParallax(parallax);
            }
        }

        var targetBlend = targetActive ? 1f : 0f;
        var step = frameTime / TransitionSeconds;

        if (_blend < targetBlend)
            _blend = Math.Min(targetBlend, _blend + step);
        else if (_blend > targetBlend)
            _blend = Math.Max(targetBlend, _blend - step);

        if (_blend <= 0f && !targetActive)
        {
            _activeParallax = null;
            _activeMarker = null;
            _activeMarkerHasLightning = false;
        }

        UpdateParallaxOverride();
        UpdateBackgroundLightning();
    }

    private void UpdateParallaxOverride()
    {
        if (_activeParallax is not { } parallax || _blend <= 0f)
        {
            _parallax.ClearParallaxOverride(ParallaxOverrideKey);
            return;
        }

        _parallax.SetParallaxOverride(ParallaxOverrideKey, parallax, ParallaxOverridePriority, _blend, replace: true);
    }

    internal bool TryGetBackgroundLightning(out NebulaBackgroundLightning lightning, out float alpha)
    {
        lightning = _backgroundLightning;
        alpha = 0f;

        if (!_parallaxEnabled ||
            !_hasBackgroundLightning ||
            _timing.CurTime >= _backgroundLightningEnd)
        {
            return false;
        }

        var duration = (float) (_backgroundLightningEnd - _backgroundLightningStart).TotalSeconds;
        if (duration <= 0f)
            return false;

        var life = (float) ((_timing.CurTime - _backgroundLightningStart).TotalSeconds / duration);
        var flicker = life < BackgroundLightningFlickerFlashEnd
            ? 1f
            : life < BackgroundLightningFlickerDimEnd
                ? BackgroundLightningFlickerDimAlpha
                : 1f - life;

        alpha = Math.Clamp(flicker * _blend, 0f, 1f);
        return alpha > BackgroundLightningVisibleAlphaThreshold;
    }

    private void UpdateBackgroundLightning()
    {
        if (_hasBackgroundLightning && _timing.CurTime >= _backgroundLightningEnd)
            _hasBackgroundLightning = false;

        if (!_parallaxEnabled)
            return;

        if (_nextBackgroundLightning == TimeSpan.Zero)
            ScheduleNextBackgroundLightning();

        if (!_activeMarkerHasLightning ||
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
        var pointCount = _random.Next(BackgroundLightningMinPoints, NebulaBackgroundLightning.MaxPoints + 1);
        var start = new Vector2(_random.NextFloat(BackgroundLightningStartMinX, BackgroundLightningStartMaxX), BackgroundLightningStartY);
        var end = new Vector2(
            Math.Clamp(
                start.X + _random.NextFloat(-BackgroundLightningEndMaxXOffset, BackgroundLightningEndMaxXOffset),
                BackgroundLightningEndMinX,
                BackgroundLightningEndMaxX),
            _random.NextFloat(BackgroundLightningEndMinY, BackgroundLightningEndMaxY));

        _backgroundLightning.PointCount = pointCount;
        for (var i = 0; i < pointCount; i++)
        {
            var t = pointCount == 1 ? 0f : i / (pointCount - 1f);
            var point = Vector2.Lerp(start, end, t);
            var jitter = MathF.Sin(t * MathF.PI) * _random.NextFloat(-BackgroundLightningJitter, BackgroundLightningJitter);
            point.X = Math.Clamp(point.X + jitter, BackgroundLightningPointMinX, BackgroundLightningPointMaxX);
            _backgroundLightning.Points[i] = point;
        }

        _backgroundLightning.BranchCount = 0;
        var branchTarget = _random.Next(1, NebulaBackgroundLightning.MaxBranches + 1);
        for (var i = 1; i < pointCount - 1 && _backgroundLightning.BranchCount < branchTarget; i++)
        {
            if (!_random.Prob(BackgroundLightningBranchChance))
                continue;

            var origin = _backgroundLightning.Points[i];
            var direction = _random.Prob(BackgroundLightningBranchDirectionChance) ? -1f : 1f;
            var branchEnd = origin + new Vector2(
                direction * _random.NextFloat(BackgroundLightningBranchMinLength, BackgroundLightningBranchMaxLength),
                _random.NextFloat(-BackgroundLightningBranchVerticalJitter, BackgroundLightningBranchVerticalJitter));
            branchEnd.X = Math.Clamp(branchEnd.X, BackgroundLightningBranchMinX, BackgroundLightningBranchMaxX);
            branchEnd.Y = Math.Clamp(branchEnd.Y, BackgroundLightningBranchMinY, BackgroundLightningBranchMaxY);

            var branchIndex = _backgroundLightning.BranchCount * 2;
            _backgroundLightning.Branches[branchIndex] = origin;
            _backgroundLightning.Branches[branchIndex + 1] = branchEnd;
            _backgroundLightning.BranchCount++;
        }
    }

    private void PlayBackgroundLightningSound()
    {
        var audioParams = AudioParams.Default
            .WithVolume(_random.NextFloat(BackgroundLightningMinVolume, BackgroundLightningMaxVolume))
            .WithPitchScale(_random.NextFloat(BackgroundLightningMinPitch, BackgroundLightningMaxPitch));

        _audio.PlayGlobal(BackgroundLightningSound, Filter.Local(), false, audioParams);
    }

    private void ScheduleNextBackgroundLightning()
    {
        var delay = _random.NextFloat((float) BackgroundLightningMinDelay.TotalSeconds, (float) BackgroundLightningMaxDelay.TotalSeconds);
        _nextBackgroundLightning = _timing.CurTime + TimeSpan.FromSeconds(delay);
    }

    private void OnParallaxEnabledChanged(bool enabled)
    {
        _parallaxEnabled = enabled;
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

    private bool TryGetMarkerData(EntProtoId marker, [NotNullWhen(true)] out ProtoId<ParallaxPrototype>? parallax, out bool hasLightning)
    {
        parallax = null;
        hasLightning = false;

        if (string.IsNullOrEmpty(marker.Id))
            return false;

        if (!_prototype.TryIndex<EntityPrototype>(marker, out var prototype))
            return false;

        if (!prototype.TryGetComponent<NebulaParallaxComponent>(out var parallaxComp, _componentFactory) ||
            string.IsNullOrEmpty(parallaxComp.Parallax))
        {
            return false;
        }

        parallax = new ProtoId<ParallaxPrototype>(parallaxComp.Parallax);
        hasLightning = prototype.TryGetComponent<NebulaLightningHazardComponent>(out _, _componentFactory);
        return true;
    }
}

internal sealed class NebulaBackgroundLightning
{
    public const int MaxPoints = 7;
    public const int MaxBranches = 3;

    public readonly Vector2[] Points = new Vector2[MaxPoints];
    public readonly Vector2[] Branches = new Vector2[MaxBranches * 2];
    public int PointCount;
    public int BranchCount;
}
