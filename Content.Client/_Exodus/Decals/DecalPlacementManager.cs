using System.Linq;
using System.Numerics;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Decals;
using Robust.Shared.Configuration;

namespace Content.Client._Exodus.Decals;

public interface IDecalPlacementManager
{
    bool EyedropperActive { get; }

    bool Stamping { get; }

    IReadOnlyList<(Vector2 Offset, Decal Decal)> Stamp { get; }

    event Action<Color>? EyedropperColorPicked;

    event Action<Decal>? DecalCopied;

    void SetActive(bool active);

    void SetEyedropper(bool active);

    void BeginStamp(IReadOnlyList<(Vector2 Offset, Decal Decal)> stamp);

    void ClearStamp();

    void NotifyEyedropperColorPicked(Color color);

    void NotifyDecalCopied(Decal decal);

    event Action? FavoritesChanged;

    IReadOnlyList<Color> Colors { get; }

    bool Contains(Color color);

    bool Toggle(Color color);
}

public sealed class DecalPlacementManager : IDecalPlacementManager
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private readonly List<(Vector2 Offset, Decal Decal)> _stamp = new();
    private bool _active;
    private bool _eyedropper;
    private bool _stamping;

    private readonly List<Color> _colors = new();
    private bool _loaded;

    public bool EyedropperActive => _eyedropper;
    public bool Stamping => _stamping && _stamp.Count > 0;
    public IReadOnlyList<(Vector2 Offset, Decal Decal)> Stamp => _stamp;

    public event Action<Color>? EyedropperColorPicked;
    public event Action<Decal>? DecalCopied;
    public event Action? FavoritesChanged;

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

    public IReadOnlyList<Color> Colors
    {
        get
        {
            EnsureLoaded();
            return _colors;
        }
    }

    public bool Contains(Color color)
    {
        EnsureLoaded();
        return _colors.Any(c => SameColor(c, color));
    }

    public bool Toggle(Color color)
    {
        EnsureLoaded();
        if (Remove(color))
            return false;

        _colors.Add(color);
        Save();
        return true;
    }

    private bool Remove(Color color)
    {
        var index = _colors.FindIndex(c => SameColor(c, color));
        if (index < 0)
            return false;

        _colors.RemoveAt(index);
        Save();
        return true;
    }

    private static bool SameColor(Color a, Color b)
        => a.ToArgb() == b.ToArgb();

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        _colors.Clear();
        var raw = _cfg.GetCVar(EXCVars.DecalFavoriteColors);
        foreach (var token in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Color.TryFromHex(token) is { } color)
                _colors.Add(color);
        }
    }

    private void Save()
    {
        _cfg.SetCVar(EXCVars.DecalFavoriteColors, string.Join(';', _colors.Select(c => c.ToHex())));
        _cfg.SaveToFile();
        FavoritesChanged?.Invoke();
    }
}
