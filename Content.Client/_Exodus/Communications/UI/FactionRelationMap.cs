using System.Numerics;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.War;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Communications.UI;

/// <summary>
/// Faction banners laid out on a circle, with a line between every pair colored by their relation.
/// Three factions form a triangle.
/// </summary>
public sealed partial class FactionRelationMap : Control
{
    private const float BannerSize = 64f;

    private static readonly Color NeutralColor = Color.FromHex("#8A93A0");
    private static readonly Color WarColor = Color.FromHex("#E23B3B");
    private static readonly Color AllianceColor = Color.FromHex("#3DDC97");

    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private ILocalizationManager _loc = default!;

    private readonly Font _font;
    private readonly SpriteSystem _sprites;
    private FactionRelationState[] _factions = Array.Empty<FactionRelationState>();
    private FactionPairRelationState[] _pairs = Array.Empty<FactionPairRelationState>();
    private Vector2[] _points = Array.Empty<Vector2>();
    private Texture?[] _banners = Array.Empty<Texture?>();
    private string[] _labels = Array.Empty<string>();

    public FactionRelationMap()
    {
        IoCManager.InjectDependencies(this);
        _sprites = _systems.GetEntitySystem<SpriteSystem>();
        _font = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 12);
        MinSize = new Vector2(252, 260);
        HorizontalExpand = true;
    }

    public void SetRelations(List<FactionRelationState> factions, List<FactionPairRelationState> pairs)
    {
        _factions = new FactionRelationState[factions.Count];
        for (var i = 0; i < factions.Count; i++)
            _factions[i] = factions[i];

        _pairs = new FactionPairRelationState[pairs.Count];
        for (var i = 0; i < pairs.Count; i++)
            _pairs[i] = pairs[i];

        _banners = new Texture?[factions.Count];
        _labels = new string[factions.Count];
        _points = new Vector2[factions.Count];
        for (var i = 0; i < factions.Count; i++)
        {
            _banners[i] = FactionBannerIcon.TryGet(_prototypes, _sprites, factions[i].Faction);
            _labels[i] = _loc.TryGetString(factions[i].ShortName, out var shortName)
                ? shortName
                : _loc.GetString(factions[i].Name);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var count = _factions.Length;
        if (count == 0)
            return;

        var center = PixelSize / 2f;
        var radius = MathF.Min(PixelWidth, PixelHeight) * 0.32f;

        for (var i = 0; i < count; i++)
        {
            var angle = -MathF.PI / 2f + i * MathF.Tau / count;
            _points[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        for (var i = 0; i < _pairs.Length; i++)
        {
            var pair = _pairs[i];
            if (!TryIndex(pair.First, out var first) || !TryIndex(pair.Second, out var second))
                continue;

            var color = pair.Relation switch
            {
                FactionRelationKind.War => WarColor,
                FactionRelationKind.Alliance => AllianceColor,
                _ => NeutralColor,
            };
            DrawThickLine(handle, _points[first], _points[second], color);
        }

        for (var i = 0; i < count; i++)
            DrawBanner(handle, _points[i], _labels[i], _banners[i]);
    }

    private void DrawBanner(DrawingHandleScreen handle, Vector2 center, string label, Texture? banner)
    {
        var size = new Vector2(BannerSize, BannerSize) * UIScale;
        var topLeft = center - size / 2f;
        handle.DrawCircle(center, 34f * UIScale, Color.FromHex("#202A38"));
        if (banner != null)
            handle.DrawTextureRect(banner, new UIBox2(topLeft, topLeft + size));

        var dimensions = handle.GetDimensions(_font, label, UIScale);
        var textPos = new Vector2(center.X - dimensions.X / 2f, topLeft.Y + size.Y + 2f);
        handle.DrawString(_font, textPos, label, UIScale, Color.White);
    }

    private static void DrawThickLine(DrawingHandleScreen handle, Vector2 start, Vector2 end, Color color)
    {
        handle.DrawLine(start, end, color);
        handle.DrawLine(start + new Vector2(0f, 1f), end + new Vector2(0f, 1f), color);
        handle.DrawLine(start + new Vector2(1f, 0f), end + new Vector2(1f, 0f), color);
    }

    private bool TryIndex(ProtoId<TerritoryFactionPrototype> faction, out int index)
    {
        for (var i = 0; i < _factions.Length; i++)
        {
            if (_factions[i].Faction != faction)
                continue;

            index = i;
            return true;
        }

        index = -1;
        return false;
    }
}
