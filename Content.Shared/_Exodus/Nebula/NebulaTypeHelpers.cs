namespace Content.Shared._Exodus.Nebula;

public static class NebulaTypeHelpers
{
    private static readonly NebulaType[] Types =
    [
        NebulaType.Blue,
        NebulaType.Red,
        NebulaType.Green,
        NebulaType.Purple,
    ];

    public static NebulaType GetRandomNebulaType(global::System.Random random)
    {
        return Types[random.Next(Types.Length)];
    }

    public static NebulaType GetOrDefault(IReadOnlyList<NebulaType> types, int index)
    {
        return index >= 0 && index < types.Count
            ? types[index]
            : NebulaType.Blue;
    }
}
