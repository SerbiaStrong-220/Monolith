using System.Collections.Generic;
using Content.Server.Power.Pow3r;
using Robust.Shared.Threading;

namespace Content.IntegrationTests.Tests._Exodus.Power;

[TestFixture]
[TestOf(typeof(BatteryRampPegSolver))]
public sealed class BatteryRampPegSolverTests
{
    private static IEnumerable<TestCaseData> NetworkGraphs()
    {
        yield return new TestCaseData(2, new[] { (0, 1), (1, 0) }, 0)
            .SetName("BatteryRampPegSolver_TwoNetworkCycle");
        yield return new TestCaseData(5, new[] { (0, 1), (1, 0), (0, 2), (0, 3), (3, 4) }, 0)
            .SetName("BatteryRampPegSolver_CycleWithBranches");
        yield return new TestCaseData(4, new[] { (0, 1), (1, 2), (2, 3) }, 0)
            .SetName("BatteryRampPegSolver_AcyclicChain");
        yield return new TestCaseData(6, new[] { (0, 1), (2, 3) }, 4)
            .SetName("BatteryRampPegSolver_IndependentNetworksShareGroup");
    }

    [TestCaseSource(nameof(NetworkGraphs))]
    public async Task TickSeparatesConnectedNetworks(
        int networkCount,
        (int Charging, int Discharging)[] connections,
        int minimumParallelGroupSize)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var parallel = server.ResolveDependency<IParallelManager>();

        await server.WaitAssertion(() =>
        {
            var state = new PowerState();
            var networks = new PowerState.Network[networkCount];
            for (var i = 0; i < networks.Length; i++)
            {
                var network = new PowerState.Network();
                state.Networks.Allocate(out var networkId) = network;
                network.Id = networkId;
                networks[i] = network;

                var load = new PowerState.Load { DesiredPower = 10f };
                state.Loads.Allocate(out var loadId) = load;
                load.Id = loadId;
                load.LinkedNetwork = networkId;
                network.Loads.Add(loadId);

                var supply = new PowerState.Supply { MaxSupply = 20f };
                state.Supplies.Allocate(out var supplyId) = supply;
                supply.Id = supplyId;
                supply.LinkedNetwork = networkId;
                network.Supplies.Add(supplyId);
            }

            foreach (var (charging, discharging) in connections)
            {
                var battery = new PowerState.Battery
                {
                    Capacity = 1000f,
                    CurrentStorage = 500f,
                    MaxChargeRate = 50f,
                    MaxSupply = 100f,
                    LinkedNetworkCharging = networks[charging].Id,
                    LinkedNetworkDischarging = networks[discharging].Id,
                };
                state.Batteries.Allocate(out var batteryId) = battery;
                battery.Id = batteryId;
                networks[charging].BatteryLoads.Add(batteryId);
                networks[discharging].BatterySupplies.Add(batteryId);
            }

            var solver = new BatteryRampPegSolver();
            // Exercise both initial grouping and the cached schedule with the actual parallel manager.
            Assert.DoesNotThrow(() => solver.Tick(0.1f, state, parallel));
            Assert.DoesNotThrow(() => solver.Tick(0.1f, state, parallel));
            Assert.That(state.GroupedNets, Is.Not.Null);

            var groupedNetworks = state.GroupedNets!;
            var groups = new Dictionary<PowerState.NodeId, int>();
            var largestGroup = 0;
            for (var i = 0; i < groupedNetworks.Count; i++)
            {
                var group = groupedNetworks[i];
                largestGroup = Math.Max(largestGroup, group.Count);
                foreach (var network in group)
                {
                    Assert.That(groups.TryAdd(network.Id, i), Is.True, "A network must be scheduled exactly once.");
                }
            }

            Assert.That(groups.Count, Is.EqualTo(networkCount));
            foreach (var (charging, discharging) in connections)
            {
                Assert.That(groups[networks[charging].Id], Is.Not.EqualTo(groups[networks[discharging].Id]),
                    "Networks sharing a battery must not execute in the same parallel group.");
            }

            Assert.That(largestGroup, Is.GreaterThanOrEqualTo(minimumParallelGroupSize));
            foreach (var network in networks)
            {
                Assert.That(state.Loads[network.Loads[0]].ReceivingPower, Is.Positive,
                    "The solver must supply the connected load, not merely build a schedule.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
