using Content.Client.Atmos.Monitor.UI;
using Content.Client.Atmos.Monitor.UI.Widgets;
using Content.Client.Message;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Atmos;

/// <summary>
/// Displays a gas reading independently of whether the sensor monitors its concentration.
/// </summary>
public sealed class AtmosSensorGasControl : BoxContainer
{
    private readonly Gas _gas;
    private readonly string _gasName;
    private readonly RichTextLabel _label = new();
    private ThresholdControl? _thresholdControl;

    public event Action<AtmosAlarmThreshold>? ThresholdChanged;

    public AtmosSensorGasControl(Gas gas, string gasName)
    {
        _gas = gas;
        _gasName = gasName;
        Name = gas.ToString();
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        AddChild(_label);
    }

    public void UpdateData(AtmosSensorData data)
    {
        Visible = data.Gases.TryGetValue(_gas, out var amount);
        if (!Visible)
            return;

        var fraction = data.TotalMoles > 0 ? amount / data.TotalMoles : 0;
        if (!data.GasThresholds.TryGetValue(_gas, out var threshold) || threshold == null)
        {
            if (_thresholdControl != null)
            {
                _thresholdControl.ThresholdDataChanged -= OnThresholdDataChanged;
                RemoveChild(_thresholdControl);
                _thresholdControl = null;
            }

            _label.SetMarkup(Loc.GetString("air-alarm-ui-gases-indicator-no-threshold",
                ("gas", _gasName),
                ("amount", $"{amount:0.####}"),
                ("percentage", $"{100 * fraction:0.##}")));
            return;
        }

        _label.SetMarkup(Loc.GetString("air-alarm-ui-gases-indicator",
            ("gas", _gasName),
            ("color", AirAlarmWindow.ColorForThreshold(fraction, threshold)),
            ("amount", $"{amount:0.####}"),
            ("percentage", $"{100 * fraction:0.##}")));

        if (_thresholdControl == null)
        {
            _thresholdControl = new ThresholdControl(
                Loc.GetString("air-alarm-ui-thresholds-gas-title", ("gas", _gasName)),
                threshold,
                AtmosMonitorThresholdType.Gas,
                _gas,
                100)
            {
                Margin = new Thickness(20, 2, 2, 2),
            };
            _thresholdControl.ThresholdDataChanged += OnThresholdDataChanged;
            AddChild(_thresholdControl);
        }

        _thresholdControl.UpdateThresholdData(threshold, fraction);
    }

    private void OnThresholdDataChanged(AtmosMonitorThresholdType type, AtmosAlarmThreshold threshold, Gas? gas)
    {
        ThresholdChanged?.Invoke(threshold);
    }
}
