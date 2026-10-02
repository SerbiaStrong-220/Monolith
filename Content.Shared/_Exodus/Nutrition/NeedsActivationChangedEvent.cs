namespace Content.Shared._Exodus.Nutrition;

/// <summary>Refreshes derived metabolic rates when a body's needs are suspended or activated.</summary>
[ByRefEvent]
public readonly record struct NeedsActivationChangedEvent;
