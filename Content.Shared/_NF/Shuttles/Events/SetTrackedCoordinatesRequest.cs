// New Frontiers - This file is licensed under AGPLv3
// Copyright (c) 2024 New Frontiers Contributors
// See AGPLv3.txt for details.

using Robust.Shared.Serialization;
using System.Numerics;

namespace Content.Shared._NF.Shuttles.Events;

[Serializable, NetSerializable]
public sealed class SetTargetCoordinatesRequest : BoundUserInterfaceMessage
{
    public NetEntity? ShuttleEntityUid;
    public Vector2 TrackedPosition;
    public NetEntity TrackedEntity = NetEntity.Invalid;
}

[Serializable, NetSerializable]
public sealed class SetHideTargetRequest : BoundUserInterfaceMessage
{
    public bool Hidden;
}
