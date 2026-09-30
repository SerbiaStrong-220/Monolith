namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>
/// Remaining target tiles of a mining target and their exposed surface, shared by every console mining it.
/// Maintained synchronously from tile-change events, so lasers never rescan the grid to pick a target.
/// </summary>
[RegisterComponent, UnsavedComponent, Access(typeof(BulkMiningSurfaceSystem))]
public sealed partial class BulkMiningSurfaceComponent : Component
{
    /// <summary>Active consoles retaining this cache. The last release removes the component.</summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Users = new();

    /// <summary>8-by-8 tile blocks of remaining target tiles, one bit per tile.</summary>
    [ViewVariables]
    public readonly Dictionary<Vector2i, ulong> Targets = new();

    /// <summary>Target tiles with at least one cardinal neighbour that is not a target tile: the minable surface.</summary>
    [ViewVariables]
    public readonly Dictionary<Vector2i, ulong> Exposed = new();

    [ViewVariables]
    public int Remaining;

    /// <summary>Whether the grid had a natural deposit when retained. Artificial grids keep every tile as a target.</summary>
    [ViewVariables]
    public bool Natural;
}
