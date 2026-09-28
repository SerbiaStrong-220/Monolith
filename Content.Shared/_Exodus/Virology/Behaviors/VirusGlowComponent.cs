// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

namespace Content.Shared._Exodus.Virology.Behaviors;

[RegisterComponent]
public sealed partial class VirusGlowComponent : Component
{
    /// <summary>Prevents replaying initialization over the state restored from a saved host.</summary>
    [DataField]
    public bool StateApplied;

    [DataField]
    public Color LightColor = Color.White;

    [DataField]
    public float LightRadius = 1.5f;

    [DataField]
    public float LightEnergy = 1f;

    /// <summary>We added host's point light, so restore removes only ours.</summary>
    [DataField]
    public bool Added;

    [DataField]
    public Color SavedColor;

    [DataField]
    public float SavedRadius;

    [DataField]
    public float SavedEnergy;

    [DataField]
    public bool SavedEnabled;
}
