namespace Content.Shared._Exodus.Genetics;

/// <summary>Innate burning sensitivity, applied after equipment protection without changing fire stacks.</summary>
[ByRefEvent]
public record struct GeneticFireDamageEvent(float Multiplier);
