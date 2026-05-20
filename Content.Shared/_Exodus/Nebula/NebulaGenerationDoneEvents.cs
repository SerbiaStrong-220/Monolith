namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Raised after <c>NebulaGenerationSystem</c> finishes placing all blob nebulas and pushing
/// summaries into <see cref="NebulaMapDataComponent"/>. Used by downstream systems that need
/// to act on the final nebula list (e.g. POI spawning).
/// </summary>
[ByRefEvent]
public readonly record struct NebulaBlobGenerationDoneEvent;

/// <summary>
/// Raised after <c>DeathZoneGenerationSystem</c> finishes generating the world-end shape and
/// writing it to <see cref="NebulaMapDataComponent"/>.
/// </summary>
[ByRefEvent]
public readonly record struct WorldEndGenerationDoneEvent;
