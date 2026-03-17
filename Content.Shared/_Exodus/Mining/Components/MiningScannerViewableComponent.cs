// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Mining.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(MiningScannerViewerSystem))]
public sealed partial class MiningScannerViewableComponent : Component;

[Serializable, NetSerializable]
public enum MiningScannerVisualLayers : byte
{
    Overlay
}
