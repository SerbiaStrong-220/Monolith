using Content.Shared.Shuttles.BUIStates;

namespace Content.Client.Shuttles.UI;

public sealed partial class DockingScreen
{
    /// <summary>
    /// Full console updates also arrive when only the autopilot's dampening mode changes.
    /// Keep the existing port instances when their values match: ShuttleDockControl uses
    /// those instances as dictionary keys and captures them in button handlers.
    /// </summary>
    private bool DockStateMatches(EntityUid? shuttle, DockingInterfaceState state)
    {
        if (DockingControl.DockState == null ||
            DockingControl.GridEntity != shuttle ||
            Docks.Count != state.Docks.Count)
        {
            return false;
        }

        foreach (var (grid, ports) in state.Docks)
        {
            if (!Docks.TryGetValue(grid, out var previousPorts) || previousPorts.Count != ports.Count)
                return false;

            for (var i = 0; i < ports.Count; i++)
            {
                var previous = previousPorts[i];
                var current = ports[i];

                if (previous.Entity != current.Entity ||
                    previous.Name != current.Name ||
                    !previous.Coordinates.Equals(current.Coordinates) ||
                    !previous.Angle.Equals(current.Angle) ||
                    previous.GridDockedWith != current.GridDockedWith ||
                    previous.LabelName != current.LabelName ||
                    previous.RadarColor != current.RadarColor ||
                    previous.HighlightedRadarColor != current.HighlightedRadarColor ||
                    previous.ReceiveOnly != current.ReceiveOnly ||
                    previous.DockType != current.DockType)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
