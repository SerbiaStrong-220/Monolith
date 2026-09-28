namespace Content.Server._Exodus.Medical.SuitSensors;

/// <summary>
/// Marks an enabled suit sensor with a carrier for periodic status updates.
/// Rebuilt from sensor mode and equipment/container events, never saved to maps.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class ActiveSuitSensorComponent : Component;
