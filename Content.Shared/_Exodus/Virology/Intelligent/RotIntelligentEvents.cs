using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Virology.Intelligent;

public sealed partial class RotOpenMenuEvent : InstantActionEvent;
public sealed partial class RotReturnToCoreEvent : InstantActionEvent;
public sealed partial class RotPilotEvent : InstantActionEvent;
public sealed partial class RotUnanchorEvent : InstantActionEvent;
public sealed partial class RotBuildEvent : WorldTargetActionEvent;
public sealed partial class RotRallyEvent : WorldTargetActionEvent;
public sealed partial class RotRepairEvent : EntityTargetActionEvent;
public sealed partial class RotDissolveEvent : EntityTargetActionEvent;
public sealed partial class RotAdoptEvent : EntityTargetActionEvent;
public sealed partial class RotToggleRootEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class RotRootingEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class RotSpreadConversionEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class RotConstructionEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public enum RotIntelligentUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class RotSelectBuildingMessage(ProtoId<RotBuildingPrototype> building, int rotation) : BoundUserInterfaceMessage
{
    public readonly ProtoId<RotBuildingPrototype> Building = building;
    public readonly int Rotation = rotation;
}

[Serializable, NetSerializable]
public sealed class RotRotateBuildingMessage(sbyte direction) : EntityEventArgs
{
    public readonly sbyte Direction = direction;
}

[Serializable, NetSerializable]
public sealed class RotJumpMessage(NetEntity source) : BoundUserInterfaceMessage
{
    public readonly NetEntity Source = source;
}

[Serializable, NetSerializable]
public sealed class RotCancelProjectMessage(NetEntity project) : BoundUserInterfaceMessage
{
    public readonly NetEntity Project = project;
}

[Serializable, NetSerializable]
public sealed class RotColonyCommandMessage(RotColonyCommand command) : BoundUserInterfaceMessage
{
    public readonly RotColonyCommand Command = command;
}

[Serializable, NetSerializable]
public enum RotColonyCommand : byte
{
    Return,
    Pilot,
    Unanchor,
    Rotate,
}

[Serializable, NetSerializable]
public sealed class RotIntelligentUiState(float biomass, float capacity, float income, int projects,
    List<RotCameraEntry> cameras, List<RotProjectEntry> jobs, string? feedback) : BoundUserInterfaceState
{
    public readonly float Biomass = biomass;
    public readonly float Capacity = capacity;
    public readonly float Income = income;
    public readonly int Projects = projects;
    public readonly List<RotCameraEntry> Cameras = cameras;
    public readonly List<RotProjectEntry> Jobs = jobs;
    public readonly string? Feedback = feedback;
}

[Serializable, NetSerializable]
public readonly record struct RotCameraEntry(NetEntity Entity, string Name, bool Alert);

[Serializable, NetSerializable]
public readonly record struct RotProjectEntry(NetEntity Entity, string Name, float Progress);

/// <summary>Only visible cells around the current eye are sent to the controller, never every camera's PVS.</summary>
[Serializable, NetSerializable]
public sealed class RotVisionEvent(NetEntity core, NetEntity? grid, List<Vector2i> tiles, uint revision) : EntityEventArgs
{
    public readonly NetEntity Core = core;
    public readonly NetEntity? Grid = grid;
    public readonly List<Vector2i> Tiles = tiles;
    public readonly uint Revision = revision;
}

[Serializable, NetSerializable]
public sealed class RotVisionRequestEvent : EntityEventArgs;
