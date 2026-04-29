namespace Content.Shared._Exodus.Nebula;

public static class NebulaTypeHelpers
{
    private static readonly NebulaType[] TestCycle =
    [
        NebulaType.Blue,
        NebulaType.Red,
        NebulaType.Green,
        NebulaType.Purple,
    ];

    public static NebulaType GetTestNebulaType(int index)
    {
        // Exodus TODO nebula-types: replace this test cycle with random type selection once generation balance is configured.
        return TestCycle[index % TestCycle.Length];
    }
}
