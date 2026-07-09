using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.FireControl;

public sealed partial class ReloadProgressButton : Button
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Server/predicted time at which gun can fire.</summary>
    public TimeSpan NextFire;

    /// <summary>Length of a full reload for this gun. Zero disables the bar.</summary>
    public TimeSpan Cooldown;

    private static readonly Color FillColor = Color.FromHex("#2B2B2B");

    public ReloadProgressButton()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (!Pressed)
            return;

        var cooldown = Cooldown.TotalSeconds;
        var remaining = (NextFire - _timing.CurTime).TotalSeconds;
        var progress = cooldown <= 0d ? 1f : (float) Math.Clamp(1d - remaining / cooldown, 0d, 1d);

        handle.DrawRect(new UIBox2(0f, 0f, PixelWidth * progress, PixelHeight), FillColor);
    }
}
