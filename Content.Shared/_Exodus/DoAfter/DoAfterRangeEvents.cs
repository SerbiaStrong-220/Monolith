using Content.Shared.DoAfter;

namespace Content.Shared._Exodus.DoAfter;

/// <summary>Raised on the user before a delayed interaction is validated and started.</summary>
[ByRefEvent]
public readonly record struct BeforeDoAfterStartEvent(DoAfterArgs Args);

/// <summary>
/// Checks that the provider of a delayed interaction's extended range still permits it.
/// A missing provider cancels the interaction. Normal range and obstruction checks still apply.
/// </summary>
[ByRefEvent]
public record struct ValidateDoAfterRangeEvent(DoAfterArgs Args, bool Handled = false, bool Cancelled = false);
