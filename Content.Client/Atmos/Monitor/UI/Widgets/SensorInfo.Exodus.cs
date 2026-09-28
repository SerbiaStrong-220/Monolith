// Exodus-begin: keep gas rows usable when a sensor has only some thresholds or readings.
using Content.Client._Exodus.Atmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Atmos.Monitor;

namespace Content.Client.Atmos.Monitor.UI.Widgets;

public sealed partial class SensorInfo
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private ILocalizationManager _localization = default!;

    private readonly Dictionary<Gas, AtmosSensorGasControl> _gasRows = new();
    private AtmosSensorData _data = default!;

    private void InitializeGasControls(AtmosSensorData data)
    {
        IoCManager.InjectDependencies(this);
        var atmosphere = _entities.System<SharedAtmosphereSystem>();
        foreach (var gas in Enum.GetValues<Gas>())
        {
            var row = new AtmosSensorGasControl(gas, AtmosGasLocalization.GetName(gas, atmosphere, _localization));
            row.ThresholdChanged += threshold =>
            {
                OnThresholdUpdate?.Invoke(_address, AtmosMonitorThresholdType.Gas, threshold, gas);
            };
            _gasRows.Add(gas, row);
            GasContainer.AddChild(row);
        }

        UpdateGasData(data);
    }

    private void UpdateGasData(AtmosSensorData data)
    {
        _data = data;
        foreach (var row in _gasRows.Values)
        {
            row.UpdateData(data);
        }
    }
}
// Exodus-end
