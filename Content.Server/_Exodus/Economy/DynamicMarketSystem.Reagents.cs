// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

public sealed partial class DynamicMarketSystem
{
    public static string ReagentKey(ProtoId<ReagentPrototype> reagent)
    {
        return $"reagent:{reagent.Id}";
    }

    public static bool IsReagentKey(string key)
    {
        return key.StartsWith("reagent:", StringComparison.Ordinal);
    }
}
