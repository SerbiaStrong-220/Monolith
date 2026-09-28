using Content.Shared._Exodus.Territory;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Communications.UI;

/// <summary>
/// Resolves the configured UI icon, falling back to the in-world banner for other factions.
/// </summary>
public static class FactionBannerIcon
{
    public static Texture? TryGet(
        IPrototypeManager prototypes,
        SpriteSystem sprites,
        ProtoId<TerritoryFactionPrototype> faction)
    {
        if (!prototypes.TryIndex(faction, out TerritoryFactionPrototype? prototype))
            return null;

        if (prototype.Icon is { } icon)
            return sprites.Frame0(icon);

        if (prototype.Banner is not { } banner ||
            !prototypes.TryIndex(banner, out EntityPrototype? entity))
        {
            return null;
        }

        return sprites.Frame0(entity);
    }
}
