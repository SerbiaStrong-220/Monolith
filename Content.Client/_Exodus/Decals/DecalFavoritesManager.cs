using System.Linq;
using Content.Shared._Exodus.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client._Exodus.Decals;
public interface IDecalFavoritesManager
{
    /// <summary>Raised whenever the favorites list changes, so open windows can refresh.</summary>
    event Action? FavoritesChanged;

    IReadOnlyList<Color> Colors { get; }

    bool Contains(Color color);

    /// <summary>Adds the color, or removes it if already a favorite. Returns true if it was added.</summary>
    bool Toggle(Color color);
}

public sealed class DecalFavoritesManager : IDecalFavoritesManager
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private readonly List<Color> _colors = new();
    private bool _loaded;

    public event Action? FavoritesChanged;

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

    /// <summary>Removes the color if present. Returns true if something was removed.</summary>
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
