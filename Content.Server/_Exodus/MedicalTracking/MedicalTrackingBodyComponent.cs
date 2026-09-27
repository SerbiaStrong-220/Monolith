namespace Content.Server._Exodus.MedicalTracking;

[RegisterComponent]
public sealed partial class MedicalTrackingBodyComponent : Component
{
    /// <summary>The body's currently installed medical tracking implant.</summary>
    [DataField]
    public EntityUid Implant;
}
