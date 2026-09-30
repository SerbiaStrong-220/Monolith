// Exodus-begin: refresh the body damage indicator when its icon theme changes.
using Content.Client._Exodus.UserInterface.StatusIcons;
using Content.Shared._Shitmed.Targeting;

namespace Content.Client._Shitmed.UserInterface.Systems.PartStatus.Widgets;

public sealed partial class PartStatusControl
{
    private Dictionary<TargetBodyPart, TargetIntegrity>? _bodyStatus;

    private StatusIconThemeUIController StatusIconTheme =>
        UserInterfaceManager.GetUIController<StatusIconThemeUIController>();

    protected override void EnteredTree()
    {
        base.EnteredTree();
        StatusIconTheme.ThemeChanged += RefreshStatusIcons;
        RefreshStatusIcons();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        StatusIconTheme.ThemeChanged -= RefreshStatusIcons;
    }

    private void RefreshStatusIcons()
    {
        if (_bodyStatus != null)
            SetTextures(_bodyStatus);
    }
}
// Exodus-end
