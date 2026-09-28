namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>Transient connectivity analysis shared by all miners of this grid.</summary>
[RegisterComponent, UnsavedComponent, Access(typeof(BulkMiningConnectivitySystem))]
public sealed partial class BulkMiningConnectivityComponent : Component
{
    /// <summary>Active consoles retaining this cache. The last release removes the component.</summary>
    public readonly HashSet<EntityUid> Users = new();

    /// <summary>Stable indices for visited tiles. Traversal state is updated directly in fixed-size pages.</summary>
    public readonly Dictionary<Vector2i, int> NodeIndices = new();

    /// <summary>Fixed-size pages avoid copying the node array when the cache grows.</summary>
    public readonly List<BulkMiningConnectivityNode[]> NodePages = new();

    public int NodeCount;

    /// <summary>Explicit DFS stack, allowing long searches to yield without recursion.</summary>
    public readonly List<int> Stack = new();

    public ulong Generation;
    public int Discovered;
    public bool Pending;
    public bool Complete;
}

public struct BulkMiningConnectivityNode
{
    public Vector2i Tile;
    public ulong Generation;
    public int Discovery;
    public int Low;
    /// <summary>Nonempty cardinal neighbors, maintained synchronously by tile-change events.</summary>
    public byte Neighbors;
    public bool NeighborsKnown;
    public byte NextNeighbor;
    /// <summary>Number of components left after removing this vertex from its connected component.</summary>
    public byte Parts;
    /// <summary>Stable neighbor indices plus one; zero means this edge has not been visited yet.</summary>
    public int East;
    public int North;
    public int West;
    public int South;
}

public enum BulkMiningTileSafety : byte
{
    Safe,
    Unsafe,
    Pending,
}
