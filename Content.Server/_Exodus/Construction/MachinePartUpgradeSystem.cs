using Content.Server.Construction;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Construction;

/// <summary>Evaluates configurable upgrade curves, weighting mixed parts by their stack quantities.</summary>
public sealed class MachinePartUpgradeSystem : EntitySystem
{
    public float GetMultiplier(IReadOnlyList<MachinePartState> parts, ProtoId<MachinePartPrototype> partType,
        Dictionary<int, float> multipliers, bool useRatingAsFallback = false)
    {
        var total = 0d;
        var count = 0;
        foreach (var part in parts)
        {
            if (part.Part.PartType != partType)
                continue;

            var quantity = part.Quantity();
            var fallback = useRatingAsFallback ? Math.Max(1, part.Part.Rating) : 1f;
            var multiplier = multipliers.GetValueOrDefault(part.Part.Rating, fallback);
            total += (double)Math.Max(0.01f, multiplier) * quantity;
            count += quantity;
        }

        return count > 0 ? (float)(total / count) : 1f;
    }
}
