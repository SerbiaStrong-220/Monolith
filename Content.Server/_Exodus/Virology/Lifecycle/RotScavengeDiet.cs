using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>What a rot scavenger may eat: which blood reagents, how much per bite and how many bites a fresh corpse holds.</summary>
public readonly record struct RotScavengeDiet(HashSet<ProtoId<ReagentPrototype>> BloodReagents, FixedPoint2 BloodPerBite, int CorpseMeals);
