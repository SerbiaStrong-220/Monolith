namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Raised on a parent after producing offspring or remains, so optional systems can inherit ownership.</summary>
[ByRefEvent]
public readonly record struct VirusOffspringSpawnedEvent(EntityUid Child);
