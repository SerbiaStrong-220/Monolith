using System.Linq;
using Content.Shared._Exodus.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client._Exodus.Decals;

/// <summary>
///     Stores the player's favorite decal colors locally. Persisted via an archived client-only
///     cvar (<see cref="XCVars.DecalFavoriteColors"/>), so favorites survive relogs and restarts.
/// </summary>
public sealed class FavoriteDecalColorsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private readonly List<Color> _colors = new();

    /// <summary>Raised whenever the favorites list changes, so open windows can refresh.</summary>
    public event Action? FavoritesChanged;

    public IReadOnlyList<Color> Colors => _colors;

    public override void Initialize()
    {
        base.Initialize();
        Load();
    }

    public bool Contains(Color color)
    {
        return _colors.Any(c => SameColor(c, color));
    }

    /// <summary>Adds the color, or removes it if already a favorite. Returns true if it is now a favorite.</summary>
    public bool Toggle(Color color)
    {
        if (Remove(color))
            return false;

        _colors.Add(color);
        Save();
        return true;
    }

    /// <summary>Removes the color if present. Returns true if something was removed.</summary>
    public bool Remove(Color color)
    {
        var index = _colors.FindIndex(c => SameColor(c, color));
        if (index < 0)
            return false;

        _colors.RemoveAt(index);
        Save();
        return true;
    }

    // Compare at the 8-bit precision colors are stored/serialized at, without allocating hex strings.
    private static bool SameColor(Color a, Color b)
        => a.RByte == b.RByte && a.GByte == b.GByte && a.BByte == b.BByte && a.AByte == b.AByte;

    private void Load()
    {
        _colors.Clear();
        var raw = _cfg.GetCVar(XCVars.DecalFavoriteColors);
        foreach (var token in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Color.TryFromHex(token) is { } color)
                _colors.Add(color);
        }
    }

    private void Save()
    {
        // Color.ToHex() already includes the leading '#', which TryFromHex requires when loading.
        _cfg.SetCVar(XCVars.DecalFavoriteColors, string.Join(';', _colors.Select(c => c.ToHex())));
        _cfg.SaveToFile();
        FavoritesChanged?.Invoke();
    }
}
