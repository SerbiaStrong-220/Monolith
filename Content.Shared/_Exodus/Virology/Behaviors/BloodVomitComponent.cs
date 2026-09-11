using Content.Shared.FixedPoint;

namespace Content.Shared._Exodus.Virology.Behaviors;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class BloodVomitComponent : Component
{
    /// <summary>Time between attacks while this symptom stage is active.</summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(45);

    /// <summary>Blood removed from the host and mixed into each vomit, limited by available blood.</summary>
    [DataField]
    public FixedPoint2 BloodAmount = 20;

    /// <summary>Scheduled attack, shifted along with the host when its map is paused.</summary>
    [DataField, AutoPausedField]
    public TimeSpan NextVomit;
}
