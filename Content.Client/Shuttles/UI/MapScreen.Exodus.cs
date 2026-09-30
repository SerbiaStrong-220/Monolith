using System.Numerics;
using Content.Shared._Exodus.MedicalTracking; // Exodus medical tablet
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map; // Exodus medical tablet

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    // Exodus-begin configurable map ping audio
    /// <summary>Whether refreshing the map plays its radar ping sound.</summary>
    public bool PlayPingSound { get; set; } = true;
    // Exodus-end

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

    // Exodus-begin medical tablet
    public void SetupMedicalMap(EntityUid entity)
    {
        PlayPingSound = false;
        SetupReadOnlyMap(entity);
        // Medical windows reserve room for triage and patient details even at their minimum size.
        MapRadar.MinSize = new Vector2(220, 180);
        MapRadar.HorizontalAlignment = HAlignment.Stretch;
        MapRadar.VerticalAlignment = VAlignment.Stretch;
        MapRadar.Margin = new Thickness(0);
    }

    public event Action<NetEntity?> MedicalContactSelected
    {
        add => MapRadar.MedicalContactSelected += value;
        remove => MapRadar.MedicalContactSelected -= value;
    }

    public void SetMedicalContacts(List<MedicalTrackingContact> contacts)
    {
        MapRadar.SetMedicalContacts(contacts);
    }

    public void FocusMedicalContact(MapCoordinates coordinates)
    {
        if (_mapManager.MapExists(coordinates.MapId))
            MapRadar.FocusMedicalPosition(coordinates, 64f);
    }

    public void SelectMedicalContact(NetEntity? body)
    {
        MapRadar.SelectMedicalContact(body);
    }

    public void FocusMedicalOperator()
    {
        if (TryGetMedicalOperatorPosition(out var coordinates))
            FocusMedicalContact(coordinates);
    }

    public void SetMedicalOperator(EntityUid? entity)
    {
        MapRadar.SetMedicalOperator(entity);
    }

    public bool TryGetMedicalOperatorPosition(out MapCoordinates coordinates)
    {
        return MapRadar.TryGetMedicalOperatorPosition(out coordinates);
    }

    // Exodus-end
}
