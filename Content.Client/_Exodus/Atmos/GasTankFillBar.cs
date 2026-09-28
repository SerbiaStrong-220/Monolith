using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.Atmos;

/// <summary>
/// A gas supply bar drawn over a hand or equipment slot.
/// </summary>
public sealed partial class GasTankFillBar : Control
{
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Color BackgroundColor = Color.FromHex("#101820E6");
    private readonly SlotControl _slot;

    public GasTankFillBar(SlotControl slot)
    {
        IoCManager.InjectDependencies(this);
        _slot = slot;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        // The game screen is built before ECS startup and survives reconnects.
        if (_slot.Entity is not { } entity ||
            !_entities.TrySystem<GasTankFillIndicatorSystem>(out var indicators) ||
            !indicators.TryGetFill(entity, out var level, out var lowPressure) ||
            !_entities.TrySystem<ProgressColorSystem>(out var progressColor))
        {
            return;
        }

        var padding = 4f * UIScale;
        var border = UIScale;
        var height = 6f * UIScale;
        var width = PixelWidth - padding * 2f;

        if (_slot.StorageButton.Visible)
            width -= 16f * UIScale;

        if (width <= border * 2f || PixelHeight < height + padding)
            return;

        var top = PixelHeight - padding - height;
        var color = progressColor.GetProgressColor(lowPressure ? 0f : level / 100f);

        var background = UIBox2.FromDimensions(padding, top, width, height);
        handle.DrawRect(background, BackgroundColor);
        handle.DrawRect(background, color, filled: false);

        if (level == 0)
            return;

        var fillWidth = (width - border * 2f) * level / 100f;
        var fill = UIBox2.FromDimensions(padding + border, top + border, fillWidth, height - border * 2f);
        handle.DrawRect(fill, color);
    }
}
