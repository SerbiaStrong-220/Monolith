using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared.Whitelist;

namespace Content.Shared._Exodus.Virology.Intelligent;

/// <summary>The living controller. Its eye has movement input but none of the station AI's access privileges.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class RotIntelligentComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid? Eye;
    [DataField, AutoNetworkedField] public float Biomass = 120;
    [DataField, AutoNetworkedField] public float Capacity = 500;
    [DataField, AutoNetworkedField] public float Income = 1;
    [DataField] public float BaseIncome = 1;
    [DataField] public int MaxProjects = 3;
    [DataField] public int MaxTerritory = 1200;
    [DataField] public int InitialRadius = 1;
    /// <summary>Maximum members indexed and cells visited per network update.</summary>
    [DataField] public int NetworkBudget = 256;
    [DataField] public float RallyRange = 24;
    [DataField] public TimeSpan RallyDuration = TimeSpan.FromSeconds(30);
    [DataField] public TimeSpan IncomeInterval = TimeSpan.FromSeconds(1);
    [DataField] public TimeSpan VisionInterval = TimeSpan.FromSeconds(0.25);
    [DataField] public TimeSpan UiInterval = TimeSpan.FromSeconds(0.5);
    [DataField] public EntProtoId Tissue = "RotTissue";
    [DataField] public EntProtoId EyePrototype = "RotIntelligentEye";
    [DataField] public EntProtoId BuildActionPrototype = "ActionRotIntelligentBuild";
    [DataField] public EntProtoId ConstructionMarker = "RotConstructionMarker";
    [DataField] public EntProtoId RallyMarker = "RotRallyMarker";
    // Non-empty list expressions emit CollectionsMarshal.SetCount, which the content sandbox forbids.
    [DataField] public EntityWhitelist ConvertibleWalls = new() { Tags = new() { "Wall" } };
    [DataField] public float RepairCostPerDamage = 0.2f;
    [DataField] public float RepairAmount = 100;
    [DataField] public TimeSpan MaintenanceDuration = TimeSpan.FromSeconds(4);
    [DataField] public float SalvageFraction = 0.5f;
    [DataField] public List<ProtoId<RotBuildingPrototype>> Buildings = [];
    [DataField, AutoNetworkedField] public ProtoId<RotBuildingPrototype> SelectedBuilding = "RotBuildTissue";
    [DataField, AutoNetworkedField] public int Rotation;
    [DataField, AutoNetworkedField] public EntityUid? BuildAction;
    [DataField, AutoNetworkedField] public bool NetworkReady;
    /// <summary>Whether the published network is safe for completing construction. New orders can always queue.</summary>
    [DataField, AutoNetworkedField] public bool ConstructionAvailable;
    [DataField, AutoNetworkedField] public bool Alive = true;
    /// <summary>The same living entity retains its colony while travelling.</summary>
    [DataField, AutoNetworkedField] public bool Rooted = true;
    [DataField, AutoNetworkedField] public bool ChangingForm;
    [DataField, AutoNetworkedField] public EntityUid? RootAction;
    [DataField] public EntProtoId RootActionPrototype = "ActionRotIntelligentRoot";
    [DataField] public TimeSpan RootDuration = TimeSpan.FromSeconds(3);
    /// <summary>Initial tissue is a one-time grant, including across map saves.</summary>
    [DataField] public bool Established;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RotIntelligentEyeComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid? Core;
}
