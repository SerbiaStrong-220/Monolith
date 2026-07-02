// Exodus parallax-overrides
using Content.Client.Parallax.Data;
using Content.Shared.Parallax;
using Robust.Shared.Prototypes;

namespace Content.Client.Parallax;

/// <summary>
/// Snapshot of the currently winning parallax override returned by
/// <see cref="ParallaxSystem.TryGetHighestParallaxOverride"/>.
/// </summary>
/// <param name="Replace">
/// If true and <paramref name="Alpha"/> is fully opaque, the base parallax can be skipped.
/// </param>
public readonly record struct ParallaxOverrideState(
    ProtoId<ParallaxPrototype> Parallax,
    ParallaxLayerPrepared[] Layers,
    int Priority,
    float Alpha,
    bool Replace);
