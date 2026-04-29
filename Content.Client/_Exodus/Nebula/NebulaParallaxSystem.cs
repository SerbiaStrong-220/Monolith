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

    [Dependency] private readonly IParallaxManager _parallax = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    public bool TryGetParallaxLayers(out ParallaxLayerPrepared[] layers)
    {
        layers = Array.Empty<ParallaxLayerPrepared>();

        if (!TryGetLocalPresence(out var presence) ||
            !TryGetParallaxPrototype(presence.Type, out var parallax))
        {
            return false;
        }

        if (!_parallax.IsLoaded(parallax))
        {
            _ = _parallax.LoadParallaxByName(parallax);
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
