using Content.Client.Parallax;
using Content.Client.Parallax.Data;
using Content.Client.Parallax.Managers;
using Content.Shared._Exodus.Nebula;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Nebula;

public sealed class NebulaParallaxSystem : EntitySystem
{
    private static readonly ProtoId<ParallaxPrototype> RedNebulaParallax = "RedNebula";
    private const float TransitionSeconds = 2f;

    [Dependency] private readonly IParallaxManager _parallax = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private ProtoId<ParallaxPrototype>? _activeParallax;
    private float _blend;

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
            // Add other nebula type parallaxes here when their assets are ready.
            case NebulaType.Red:
                parallax = RedNebulaParallax;
                return true;
            default:
                parallax = default;
                return false;
        }
    }
}
