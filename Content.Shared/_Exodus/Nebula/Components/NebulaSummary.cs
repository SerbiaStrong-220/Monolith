using Content.Shared._Exodus.Nebula.Generation;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Nebula.Components;

/// <summary>
/// Networked snapshot of a single nebula on a map. Pre-baked from the marker entity's
/// per-effect components at generation time so the client can answer FTL/parallax/visual
/// questions without seeing the marker entity itself in PVS.
/// </summary>
/// <param name="Parallax">Parallax prototype id (raw string because the prototype type is client-only).</param>
[Serializable, NetSerializable]
public readonly record struct NebulaSummary(
    NebulaShape Shape,
    EntProtoId Marker,
    bool BlocksFTL,
    string? Parallax,
    Color RadarColor);
