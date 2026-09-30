namespace Content.Shared._Exodus.Mining.AutoMining;

/// <summary>Bonus of a mining consortium with diminishing returns for every additional ship.</summary>
public static class BulkMiningLinkBonus
{
    /// <summary>
    /// <c>bonus × (1 + decay + decay² + …)</c> with one term per linked ship beyond the first, capped at
    /// <paramref name="maxBonus"/>. A lone ship gets nothing.
    /// </summary>
    public static float Get(int ships, float bonus, float decay, float maxBonus)
    {
        if (ships < 2 || !float.IsFinite(bonus) || bonus <= 0)
            return 0f;

        decay = float.IsFinite(decay) ? Math.Clamp(decay, 0f, 1f) : 0f;
        var cap = float.IsFinite(maxBonus) ? Math.Max(0f, maxBonus) : float.MaxValue;
        var total = 0f;
        var term = bonus;
        for (var ship = 1; ship < ships && total < cap && term > 0.00001f; ship++)
        {
            total += term;
            term *= decay;
        }

        return Math.Min(total, cap);
    }
}
