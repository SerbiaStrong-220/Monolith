using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Hud;
using Content.Shared.Alert;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Exodus.UserInterface.StatusIcons;

/// <summary>
/// Resolves the local status icon theme and notifies visible controls when it changes.
/// </summary>
public sealed partial class StatusIconThemeUIController : UIController
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public event Action? ThemeChanged;

    public StatusIconThemePrototype Theme
    {
        get
        {
            ProtoId<StatusIconThemePrototype> themeId = _config.GetCVar(EXCVars.StatusIconTheme);
            if (_prototypes.TryIndex(themeId, out var theme))
                return theme;

            ProtoId<StatusIconThemePrototype> defaultThemeId = EXCVars.StatusIconTheme.DefaultValue;
            return _prototypes.Index(defaultThemeId);
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        _config.OnValueChanged(EXCVars.StatusIconTheme, OnThemeChanged);
    }

    private void OnThemeChanged(string theme)
    {
        ThemeChanged?.Invoke();
    }

    public SpriteSpecifier GetAlertIcon(AlertPrototype alert, short? severity)
    {
        var defaultIcon = alert.GetIcon(severity);
        if (!Theme.AlertIcons.TryGetValue(alert.ID, out var icons))
            return defaultIcon;

        var index = alert.SupportsSeverity ? (severity ?? alert.MinSeverity) - alert.MinSeverity : 0;
        if (index < 0 || index >= icons.Count)
            return defaultIcon;

        return icons[index];
    }
}
