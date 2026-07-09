using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Nebula.Generation;

/// <summary>
/// One sinusoidal boundary modifier for an Exodus space nebula.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct NebulaWave(float Amplitude, float Frequency, float Phase);
