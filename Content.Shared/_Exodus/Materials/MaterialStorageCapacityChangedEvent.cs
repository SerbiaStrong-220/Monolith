namespace Content.Shared._Exodus.Materials;

/// <summary>Raised after the local capacity changes, without changing stored material amounts.</summary>
[ByRefEvent]
public readonly record struct MaterialStorageCapacityChangedEvent;
