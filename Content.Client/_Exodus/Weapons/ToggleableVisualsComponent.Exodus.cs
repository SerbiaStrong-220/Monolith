using Content.Shared.Hands.Components;

namespace Content.Client.Toggleable;

public sealed partial class ToggleableVisualsComponent
{
    /// <summary>Optional held-layer overrides keyed by Item.HeldPrefix (for example, a wielded pose).</summary>
    [DataField]
    public Dictionary<string, Dictionary<HandLocation, List<PrototypeLayerData>>> InhandVisualsByPrefix = new();
}
