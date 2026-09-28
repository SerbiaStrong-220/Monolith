using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Content.Client._Exodus.Atmos;
using Content.Client.Atmos.Monitor.UI.Widgets;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Monitor.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Piping.Unary.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class AirAlarmGasTest
{
    [TestCase("ru-RU")]
    [TestCase("en-US")]
    public async Task SensorHandlesPartialUpdatesAndCopiesCurrentSettings(string culture)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        SensorInfo sensor = null;
        ScrubberControl scrubber = null;
        CheckBox enabled = null;
        Button copy = null;
        AtmosSensorData latest = null;
        AtmosSensorData copied = null;
        AtmosAlarmThreshold edited = null;
        var previous = new AtmosAlarmThreshold();
        var replacement = new AtmosAlarmThreshold();
        var loc = client.ResolveDependency<ILocalizationManager>();
        var originalCulture = loc.DefaultCulture;
        try
        {
            await client.WaitAssertion(() =>
            {
                loc.SetCulture(CultureInfo.GetCultureInfo(culture));
                var gases = Enum.GetValues<Gas>().ToDictionary(gas => gas, _ => 0f);
                gases[Gas.Oxygen] = 10;
                sensor = new SensorInfo(Data(gases, new()), "sensor");
                sensor.SensorDataCopied += data => copied = data;
                sensor.OnThresholdUpdate += (_, _, threshold, _) => edited = threshold;
                var row = FindExhaustRow(sensor);
                Assert.That(row.Visible, Is.True);
                Assert.That(row.Children.OfType<ThresholdControl>(), Is.Empty);
                Assert.That(row.Children.OfType<RichTextLabel>().Single().GetMessage(), Does.Contain("ClF₃"));

                var thresholds = new Dictionary<Gas, AtmosAlarmThreshold> { [Gas.ChlorineTrifluoride] = previous };
                sensor.ChangeData(Data(gases, thresholds));
                var editor = row.Children.OfType<ThresholdControl>().Single();
                latest = Data(gases, new() { [Gas.ChlorineTrifluoride] = replacement });
                sensor.ChangeData(latest);
                Assert.That(row.Children.OfType<ThresholdControl>().Single(), Is.SameAs(editor));
                enabled = editor.FindControl<CheckBox>("CEnabled");
                enabled.Mode = BaseButton.ActionMode.Press;
                enabled.MuteSounds = true;
                copy = sensor.FindControl<Button>("CCopySettings");
                copy.Mode = BaseButton.ActionMode.Press;

                scrubber = new ScrubberControl(new GasVentScrubberData(), "scrubber");
                var atmosphere = client.EntMan.System<SharedAtmosphereSystem>();
                var gasButtons = scrubber.FindControl<GridContainer>("CGasContainer").Children.OfType<Button>()
                    .ToDictionary(button => button.Name);
                foreach (var gas in Enum.GetValues<Gas>())
                {
                    var button = gasButtons[gas.ToString()];
                    Assert.That(button.Text, Is.Not.Empty.And.Not.Contains("atmos-gas-"));
                }

                Assert.That(gasButtons[nameof(Gas.ChlorineTrifluoride)].Text, Is.EqualTo("ClF₃"));
                if (culture == "en-US")
                {
                    Assert.That(gasButtons[nameof(Gas.Oxygen)].Text,
                        Is.EqualTo(loc.GetString(atmosphere.GetGas(Gas.Oxygen).Name)),
                        "A gas without a short UI translation must use its prototype's localized name.");
                }
            });

            await client.DoGuiEvent(enabled, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, default, default));
            await client.DoGuiEvent(enabled, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, default, default));
            await client.DoGuiEvent(copy, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, default, default));
            await client.DoGuiEvent(copy, new GUIBoundKeyEventArgs(
                EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, default, default));
            await client.WaitAssertion(() =>
            {
                Assert.That(edited, Is.SameAs(replacement));
                Assert.That(replacement.Ignore, Is.True);
                Assert.That(previous.Ignore, Is.False, "Editing a refreshed control must not mutate its old snapshot.");
                Assert.That(copied, Is.SameAs(latest), "The copy button must use the latest sensor snapshot.");

                var row = FindExhaustRow(sensor);
                var editor = row.Children.OfType<ThresholdControl>().Single();
                var emptyMixture = Data(new() { [Gas.ChlorineTrifluoride] = 0 }, new(), 0);
                sensor.ChangeData(emptyMixture);
                Assert.That(row.Children.OfType<ThresholdControl>(), Is.Empty);
                Assert.That(editor.Parent, Is.Null);
                Assert.That(row.Children.OfType<RichTextLabel>().Single().GetMessage(),
                    Does.Contain("(0%)").And.Not.Contains("NaN").And.Not.Contains("Infinity"));

                var missingReading = Data(new(), latest.GasThresholds, 0);
                sensor.ChangeData(missingReading);
                Assert.That(row.Visible, Is.False, "A missing reading must not leave a stale concentration visible.");

                sensor.ChangeData(latest);
                Assert.That(row.Visible, Is.True);
                Assert.That(row.Children.OfType<ThresholdControl>().Count(), Is.EqualTo(1));
                Assert.That(row.Children.OfType<ThresholdControl>().Single(), Is.Not.SameAs(editor));

                latest.GasThresholds[Gas.ChlorineTrifluoride] = null;
                latest.Gases[(Gas) 127] = 1;
                sensor.ChangeData(latest);
                Assert.That(row.Children.OfType<ThresholdControl>(), Is.Empty);
            });
        }
        finally
        {
            await client.WaitPost(() =>
            {
                sensor?.Orphan();
                scrubber?.Orphan();
                loc.DefaultCulture = originalCulture;
            });
        }

        await pair.CleanReturnAsync();
    }

    [TestCase("AirSensor", false)]
    [TestCase("AirSensorVox", false)]
    [TestCase("AirSensorDistroStore", false)]
    [TestCase("AirSensorIndustrialStore", true)]
    public async Task SensorPrototypesConfigureAllGases(string prototype, bool ignoreExhaust)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, 0, 0));
            var monitor = em.GetComponent<AtmosMonitorComponent>(uid);
            Assert.That(monitor.GasThresholds.Keys, Is.EquivalentTo(Enum.GetValues<Gas>()));
            var threshold = monitor.GasThresholds[Gas.ChlorineTrifluoride];
            Assert.That(threshold.Ignore, Is.EqualTo(ignoreExhaust));
            Assert.That(threshold.CheckThreshold(0, out _), Is.False);
            Assert.That(threshold.CheckThreshold(0.001f, out var alarm), Is.EqualTo(!ignoreExhaust));
            Assert.That(alarm, Is.EqualTo(ignoreExhaust ? AtmosAlarmType.Normal : AtmosAlarmType.Danger));
            em.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CopyingPartialThresholdsPreservesOtherSettingsAndRejectsInvalidGases()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var uid = em.SpawnEntity("AirSensor", new EntityCoordinates(map.Grid, 0, 0));
            var monitor = em.GetComponent<AtmosMonitorComponent>(uid);
            var system = em.System<AtmosMonitorSystem>();
            var oxygen = monitor.GasThresholds[Gas.Oxygen];
            var water = monitor.GasThresholds[Gas.WaterVapor];
            var exhaust = new AtmosAlarmThreshold { UpperBound = new AlarmThresholdSetting { Value = 0.25f } };
            var data = Data(new(), new()
            {
                [Gas.ChlorineTrifluoride] = exhaust,
                [Gas.WaterVapor] = null,
                [(Gas) 127] = new(),
            });

            system.SetAllThresholds(uid, data);
            var copied = monitor.GasThresholds[Gas.ChlorineTrifluoride];
            Assert.That(copied.UpperBound.Value, Is.EqualTo(0.25f));
            Assert.That(copied, Is.Not.SameAs(exhaust));
            Assert.That(monitor.TemperatureThreshold, Is.Not.SameAs(data.TemperatureThreshold));
            Assert.That(monitor.PressureThreshold, Is.Not.SameAs(data.PressureThreshold));
            Assert.That(monitor.GasThresholds[Gas.Oxygen], Is.SameAs(oxygen));
            Assert.That(monitor.GasThresholds[Gas.WaterVapor], Is.SameAs(water));
            Assert.That(monitor.GasThresholds.ContainsKey((Gas) 127), Is.False);

            exhaust.Ignore = true;
            Assert.That(copied.Ignore, Is.False, "Copied settings must not alias the source's mutable objects.");
            system.SetAllThresholds(uid, Data(new(), new()));
            Assert.That(monitor.GasThresholds[Gas.ChlorineTrifluoride], Is.SameAs(copied));
            system.SetThreshold(uid, AtmosMonitorThresholdType.Gas, new(), (Gas) 127);
            system.SetThreshold(uid, AtmosMonitorThresholdType.Gas, new(), (Gas) (-1));
            Assert.That(monitor.GasThresholds.Keys, Is.EquivalentTo(Enum.GetValues<Gas>()));
            em.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    private static AtmosSensorData Data(
        Dictionary<Gas, float> gases,
        Dictionary<Gas, AtmosAlarmThreshold> thresholds,
        float totalMoles = 10)
    {
        return new AtmosSensorData(101.325f, Atmospherics.T20C, totalMoles, AtmosAlarmType.Normal,
            gases, new(), new(), thresholds);
    }

    private static AtmosSensorGasControl FindExhaustRow(SensorInfo sensor)
    {
        return sensor.FindControl<BoxContainer>("GasContainer").Children.OfType<AtmosSensorGasControl>()
            .Single(row => row.Name == nameof(Gas.ChlorineTrifluoride));
    }
}
