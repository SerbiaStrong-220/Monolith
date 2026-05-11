using Content.Server._Exodus.Shuttles.Systems;
using Robust.Shared.GameObjects;

namespace Content.Server._Exodus.Shuttles.Components;

/// <summary>
/// When present on a thruster, registers it in all 4 linear thrust directions simultaneously.
/// Nozzle direction and space exposure checks are suppressed in examine.
/// </summary>
[RegisterComponent]
[Access(typeof(OmnidirectionalThrusterSystem))]
public sealed partial class OmnidirectionalThrusterComponent : Component;
