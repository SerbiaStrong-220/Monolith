using Content.Shared._Exodus.Shuttles;
using Content.Shared.Shuttles.Components;

namespace Content.Shared.Shuttles.Systems;

// Exodus-begin affiliation-aware radar presentation.
public abstract partial class SharedShuttleSystem
{
    [Dependency] private IffAffiliationSystem _iffAffiliation = default!;

    public bool UsesFactionIffColor(EntityUid grid)
    {
        return _iffAffiliation.UsesFactionColor(grid);
    }

    /// <summary>
    /// FTL labels show only a grid name and, when present, its corporate controller.
    /// Viewing the map from that grid must not suppress its corporate affiliation.
    /// </summary>
    public string? GetFtlIFFLabel(EntityUid grid, bool self = false, IFFComponent? component = null)
    {
        if (!self && Resolve(grid, ref component, false) &&
            (component.Flags & (IFFFlags.HideLabel | IFFFlags.Hide)) != 0)
        {
            return null;
        }

        var name = MetaData(grid).EntityName;
        if (string.IsNullOrEmpty(name))
            name = Loc.GetString("shuttle-console-unknown");

        name = _iffAffiliation.GetGridName(grid, name);

        if (!_iffAffiliation.HasCorporateControlLabel(grid) ||
            !_iffAffiliation.TryGetLabel(grid, out var affiliation))
        {
            return name;
        }

        return Loc.GetString("exodus-iff-affiliation-label", ("name", name), ("affiliation", affiliation));
    }
}
// Exodus-end
