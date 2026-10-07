// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Exodus.Economy.UI;

/// <summary>
/// Shared visual language for sector trade terminals
/// (cargo order, pallet sell, market rebuy, Edison gas sale).
/// </summary>
public static class MarketTerminalTheme
{
    // Surfaces
    public static readonly Color BgDeep = Color.FromHex("#121216");
    public static readonly Color BgPanel = Color.FromHex("#1A1A1E");
    public static readonly Color BgCard = Color.FromHex("#1E1E24");
    public static readonly Color BgCardHover = Color.FromHex("#26262E");
    public static readonly Color BgHeader = Color.FromHex("#16161C");

    // Chrome
    public static readonly Color Border = Color.FromHex("#3A3A44");
    public static readonly Color BorderAccent = Color.FromHex("#3D6FA8");
    public static readonly Color Accent = Color.FromHex("#5B9FD4");
    public static readonly Color AccentMuted = Color.FromHex("#3A6A8C");

    // Text
    public static readonly Color TextPrimary = Color.FromHex("#E8E8EC");
    public static readonly Color TextMuted = Color.FromHex("#9A9AA8");

    // Market signals (ledger-style)
    public static readonly Color TrendUp = Color.FromHex("#80FF80");
    public static readonly Color TrendDown = Color.FromHex("#FF8080");
    public static readonly Color TrendFlat = Color.FromHex("#A0A0A0");

    public const int SidebarCategoryMaxChars = 18;

    public static StyleBoxFlat MakePanelBox(bool deep = false)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = deep ? BgDeep : BgPanel,
            BorderColor = Border,
            BorderThickness = new Thickness(1),
        };
    }

    public static StyleBoxFlat MakeCardBox()
    {
        return new StyleBoxFlat
        {
            BackgroundColor = BgCard,
            BorderColor = Border,
            BorderThickness = new Thickness(1),
        };
    }

    public static StyleBoxFlat MakeHeaderBox()
    {
        return new StyleBoxFlat
        {
            BackgroundColor = BgHeader,
            BorderColor = BorderAccent,
            BorderThickness = new Thickness(1),
        };
    }

    public static StyleBoxFlat MakeIconWellBox()
    {
        return new StyleBoxFlat
        {
            BackgroundColor = BgDeep,
            BorderColor = Border,
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>
    /// Apply rising / falling / flat label text and color.
    /// </summary>
    public static void ApplyTrend(Label label, float trend, double changePercent, bool hideWhenFlat = false)
    {
        if (hideWhenFlat && Math.Abs(trend) < 0.00005f)
        {
            label.Visible = false;
            label.Text = string.Empty;
            return;
        }

        label.Visible = true;
        var formattedPercent = changePercent.ToString("+0.0;-0.0;0.0");

        if (trend > 0.00005f)
        {
            label.Text = Loc.GetString("economy-market-trend-up", ("percent", formattedPercent));
            label.FontColorOverride = TrendUp;
        }
        else if (trend < -0.00005f)
        {
            label.Text = Loc.GetString("economy-market-trend-down", ("percent", formattedPercent));
            label.FontColorOverride = TrendDown;
        }
        else
        {
            label.Text = Loc.GetString("economy-market-trend-flat", ("percent", formattedPercent));
            label.FontColorOverride = TrendFlat;
        }
    }

    public static void ApplyTrendOptional(Label label, float trend, double? changePercent)
    {
        if (changePercent is not { } percent)
        {
            label.Visible = false;
            label.Text = string.Empty;
            return;
        }

        ApplyTrend(label, trend, percent);
    }

    /// <summary>
    /// Short label for narrow category sidebar; full name stays in tooltip.
    /// </summary>
    public static string ShortenCategoryLabel(string displayName)
    {
        if (string.IsNullOrEmpty(displayName) || displayName.Length <= SidebarCategoryMaxChars)
            return displayName;

        return displayName[..(SidebarCategoryMaxChars - 1)] + "…";
    }
}
