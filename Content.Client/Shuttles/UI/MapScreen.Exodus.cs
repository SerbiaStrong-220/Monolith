using System.Numerics;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Systems;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    // Exodus read-only bluespace map
    /// <summary>
    /// Configures the existing shuttle map as a read-only sector map.
    /// </summary>
    public void SetupReadOnlyMap(EntityUid entity)
    {
        SetConsole(entity);
        SetShuttle(entity);
        UpdateState(new ShuttleMapInterfaceState(FTLState.Available, default, [], []));
        MapRadar.FtlMode = false;
        MapRadar.ShowFTLRangeOnly = false;
        Startup();
        PingMap();
        MapRadar.MaxSize = new Vector2(float.PositiveInfinity);

        if (RightDisplayMap.Parent?.Parent is { } rightPanel)
            rightPanel.Visible = false;
    }
}
