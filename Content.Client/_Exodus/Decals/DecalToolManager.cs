using System.Numerics;
using Content.Shared.Decals;

namespace Content.Client._Exodus.Decals;

public interface IDecalToolManager
{
    /// <summary>Is eyedropper currently armed.</summary>
    bool EyedropperActive { get; }

    /// <summary>Is multi-decal stamp currently held and ready to be placed.</summary>
    bool Stamping { get; }

    /// <summary>Decals making up held stamp, as offsets from stamp origin.</summary>
    IReadOnlyList<(Vector2 Offset, Decal Decal)> Stamp { get; }

    /// <summary>Raised when eyedropper copies a color from a decal.</summary>
    event Action<Color>? EyedropperColorPicked;

    /// <summary>Raised when a single decal is copied from the map, so window can mirror its settings.</summary>
    event Action<Decal>? DecalCopied;

    /// <summary>Mirrors placer's active state; deactivating drops any armed tool.</summary>
    void SetActive(bool active);

    /// <summary>Arms or disarms eyedropper (only while placer is active). Drops any held stamp.</summary>
    void SetEyedropper(bool active);

    /// <summary>Holds a copied stack of decals as stamp ready to place. Drops eyedropper.</summary>
    void BeginStamp(IReadOnlyList<(Vector2 Offset, Decal Decal)> stamp);

    /// <summary>Drops any held stamp.</summary>
    void ClearStamp();

    /// <summary>Called by placement system after a successful sample.</summary>
    void NotifyEyedropperColorPicked(Color color);

    /// <summary>Called by placement system after a successful copy.</summary>
    void NotifyDecalCopied(Decal decal);
}

public sealed class DecalToolManager : IDecalToolManager
{
    private readonly List<(Vector2 Offset, Decal Decal)> _stamp = new();
    private bool _active;
    private bool _eyedropper;
    private bool _stamping;

    public bool EyedropperActive => _eyedropper;
    public bool Stamping => _stamping && _stamp.Count > 0;
    public IReadOnlyList<(Vector2 Offset, Decal Decal)> Stamp => _stamp;

    public event Action<Color>? EyedropperColorPicked;
    public event Action<Decal>? DecalCopied;

    public void SetActive(bool active)
    {
        _active = active;
        _eyedropper = false;
        ClearStamp();
    }

    public void SetEyedropper(bool active)
    {
        _eyedropper = active && _active;

        if (_eyedropper)
            ClearStamp();
    }

    public void BeginStamp(IReadOnlyList<(Vector2 Offset, Decal Decal)> stamp)
    {
        _eyedropper = false;
        _stamp.Clear();
        _stamp.AddRange(stamp);
        _stamping = _stamp.Count > 0;
    }

    public void ClearStamp()
    {
        _stamping = false;
        _stamp.Clear();
    }

    public void NotifyEyedropperColorPicked(Color color) => EyedropperColorPicked?.Invoke(color);

    public void NotifyDecalCopied(Decal decal) => DecalCopied?.Invoke(decal);
}
