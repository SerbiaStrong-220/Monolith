using Content.Shared.Chemistry.Components;

namespace Content.Shared._Exodus.Nutrition;

/// <summary>Allows effects to contribute to the vomit solution before it spills.</summary>
[ByRefEvent]
public readonly record struct VomitEvent(Solution Solution);
