using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.FireControl;

public sealed partial class ReloadProgressButton : Button
{
    [Dependency] private IGameTiming _timing = default!;

    private TimeSpan _nextFire;
    private TimeSpan _cooldownStart;

    public TimeSpan NextFire
    {
        get => _nextFire;
        set
        {
            var now = _timing.CurTime;

            if (value > _nextFire
                && (_nextFire <= now || value - now > _nextFire - _cooldownStart))
                _cooldownStart = now;

            _nextFire = value;
        }
    }

    private static readonly Color FillColor = Color.FromHex("#2B2B2B");

    public ReloadProgressButton()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // Only selected guns show a reload bar.
        if (!Pressed)
            return;

        var total = (_nextFire - _cooldownStart).TotalSeconds;
        var elapsed = (_timing.CurTime - _cooldownStart).TotalSeconds;
        var progress = total <= 0d ? 1f : (float) Math.Clamp(elapsed / total, 0d, 1d);

        handle.DrawRect(new UIBox2(0f, 0f, PixelWidth * progress, PixelHeight), FillColor);
    }
}
