using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.SafetyDepositBox;

[Serializable, NetSerializable]
public sealed class AdminSafetyDepositRefreshMessage : EuiMessageBase;

[Serializable, NetSerializable]
public sealed class AdminSafetyDepositSearchMessage(string query) : EuiMessageBase
{
    public string Query { get; } = query;
}

[Serializable, NetSerializable]
public sealed class AdminSafetyDepositSelectPlayerMessage(Guid userId) : EuiMessageBase
{
    public Guid UserId { get; } = userId;
}

[Serializable, NetSerializable]
public sealed class AdminSafetyDepositSelectBoxMessage(Guid boxId) : EuiMessageBase
{
    public Guid BoxId { get; } = boxId;
}

[Serializable, NetSerializable]
public enum AdminSafetyDepositAction : byte
{
    Add,
    Withdraw,
    Delete,
    AddFromHand,
}

[Serializable, NetSerializable]
public sealed class AdminSafetyDepositModifyMessage(
    Guid boxId,
    Guid viewId,
    AdminSafetyDepositAction action,
    int recordId,
    NetEntity? entity,
    string prototypeId,
    string reason) : EuiMessageBase
{
    public Guid BoxId { get; } = boxId;
    public Guid ViewId { get; } = viewId;
    public AdminSafetyDepositAction Action { get; } = action;
    public int RecordId { get; } = recordId;
    public NetEntity? Entity { get; } = entity;
    public string PrototypeId { get; } = prototypeId;
    public string Reason { get; } = reason;
}
