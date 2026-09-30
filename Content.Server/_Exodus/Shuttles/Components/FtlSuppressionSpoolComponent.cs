using Content.Server._Exodus.Shuttles.Systems;

namespace Content.Server._Exodus.Shuttles.Components;

/// <summary>
/// Marks a shuttle whose console-initiated FTL jump is still spooling up.
/// While present, an active FTL suppressor covering the shuttle or its target aborts the jump.
/// </summary>
[RegisterComponent]
[Access(typeof(FtlSuppressorSystem))]
public sealed partial class FtlSuppressionSpoolComponent : Component;
