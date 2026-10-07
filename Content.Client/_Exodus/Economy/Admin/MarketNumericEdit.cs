using System.Globalization;
using Content.Client._Exodus.Economy.UI;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;

namespace Content.Client._Exodus.Economy.Admin;

/// <summary>
/// Edits a displayed number without rounding the raw setting until the text actually changes.
/// </summary>
public sealed partial class MarketNumericEdit : BoxContainer
{
    [Dependency] private ILocalizationManager _localization = default!;

    public readonly LineEdit Input = new() { HorizontalExpand = true, MinWidth = 100 };
    public event Action? Changed;
    private readonly bool _pressure;
    private readonly bool _factor;
    private readonly double _scale;
    private readonly Label _factorValue;
    private double _referenceVolume = 100;
    private double _original;
    private string? _originalText;

    public MarketNumericEdit(bool pressure = false, double scale = 1, bool factor = false)
    {
        IoCManager.InjectDependencies(this);
        _pressure = pressure;
        _factor = factor;
        _scale = scale;
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 2;
        var input = new BoxContainer { SeparationOverride = 4 };
        input.AddChild(Input);
        if (pressure || factor || scale == 100)
            input.AddChild(new Label { Text = Loc.GetString("economy-admin-number-percent") });
        AddChild(input);
        _factorValue = new Label
        {
            Visible = pressure || factor,
            ClipText = true,
            FontColorOverride = MarketTerminalTheme.TextMuted,
            MouseFilter = MouseFilterMode.Stop,
        };
        AddChild(_factorValue);
        Input.OnTextChanged += _ =>
        {
            UpdateFactor();
            Changed?.Invoke();
        };
    }

    public void SetValue(double value)
    {
        _original = value;
        var displayed = _pressure ? MarketAdminFormatting.RisePercent(value, _referenceVolume)
            : _factor ? MarketAdminFormatting.FactorChange(value) : value * _scale;
        _originalText = MarketAdminFormatting.Number(displayed, _localization.DefaultCulture ?? CultureInfo.InvariantCulture, signed: _factor);
        Input.Text = _originalText;
        UpdateFactor();
    }

    public void SetReferenceVolume(double reference)
    {
        if (!_pressure || !double.IsFinite(reference) || reference <= 0 || reference == _referenceVolume)
            return;

        var valid = TryGetValue(out var value);
        _referenceVolume = reference;
        if (valid)
            SetValue(value);
        else
        {
            // An invalid draft remains editable, but the old displayed text must no longer resolve
            // to a raw value calculated with the previous reference volume.
            _originalText = null;
            UpdateFactor();
        }
    }

    public bool TryGetValue(out double value)
    {
        value = _original;
        if (Input.Text == _originalText)
            return double.IsFinite(value) && (!_pressure || value >= 0) && (!_factor || value > 0);

        if (!double.TryParse(Input.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var displayed) ||
            !double.IsFinite(displayed) || (_pressure && displayed < 0))
            return false;

        value = _pressure ? MarketAdminFormatting.StrengthFromPercent(displayed, _referenceVolume)
            : _factor ? 1 + displayed / 100 : displayed / _scale;
        return double.IsFinite(value) && (!_factor || value > 0);
    }

    private void UpdateFactor()
    {
        if (!_factorValue.Visible)
            return;

        if (!TryGetValue(out var value))
        {
            _factorValue.Text = string.Empty;
            _factorValue.ToolTip = null;
            return;
        }

        var factor = _pressure ? Math.Exp(value / _referenceVolume) : value;
        _factorValue.Text = Loc.GetString("economy-admin-number-factor",
            ("factor", MarketAdminFormatting.Factor(factor, _localization.DefaultCulture ?? CultureInfo.InvariantCulture)));
        _factorValue.ToolTip = _factorValue.Text;
    }
}
