// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Economy.UI;

/// <summary>
/// Panel with standard sector-terminal chrome (usable from XAML).
/// </summary>
public sealed class MarketTerminalPanel : PanelContainer
{
    private bool _deep;
    private bool _header;

    /// <summary>
    /// Darker fill (sidebar / nested well).
    /// </summary>
    public bool Deep
    {
        get => _deep;
        set
        {
            _deep = value;
            RefreshPanel();
        }
    }

    /// <summary>
    /// Header strip with accent border.
    /// </summary>
    public bool Header
    {
        get => _header;
        set
        {
            _header = value;
            RefreshPanel();
        }
    }

    public MarketTerminalPanel()
    {
        RefreshPanel();
    }

    private void RefreshPanel()
    {
        PanelOverride = _header
            ? MarketTerminalTheme.MakeHeaderBox()
            : MarketTerminalTheme.MakePanelBox(_deep);
    }
}
