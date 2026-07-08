using System.Numerics;
using Content.Shared._Mono.Radar;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Nebula.Components;

/// <summary>
/// Pre-baked radar visualization data for BSS/FTL nebula map requests.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct NebulaRadarBlipSummary(
    Vector2 Position,
    BlipConfig Config);
