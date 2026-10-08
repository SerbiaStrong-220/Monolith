using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.SafetyDepositBox;

[Serializable, NetSerializable]
public readonly record struct AdminSafetyDepositPlayer(Guid UserId, string Name, int BoxCount);

[Serializable, NetSerializable]
public readonly record struct AdminSafetyDepositBox(
    Guid BoxId,
    int CharacterIndex,
    string OwnerName,
    string ProtoId,
    string? Nickname,
    AdminSafetyDepositBoxStatus Status,
    int StoredItemCount,
    NetEntity? Entity);

[Serializable, NetSerializable]
public enum AdminSafetyDepositBoxStatus : byte
{
    Stored,
    Withdrawn,
    Lost,
    Unavailable,
}

[Serializable, NetSerializable]
public readonly record struct AdminSafetyDepositItem(int RecordId, NetEntity? Entity, string Name, string ProtoId, int Count);

[Serializable, NetSerializable]
public readonly record struct AdminSafetyDepositAuditEntry(DateTime Date, string Admin, string Action, string Result, string Details);

[Serializable, NetSerializable]
public sealed class AdminSafetyDepositEuiState : EuiStateBase
{
    public List<AdminSafetyDepositPlayer> Players = [];
    public List<AdminSafetyDepositBox> Boxes = [];
    public List<AdminSafetyDepositItem> Items = [];
    public List<AdminSafetyDepositAuditEntry> Audit = [];
    public Guid? SelectedUser;
    public string SelectedName = string.Empty;
    public Guid? SelectedBox;
    public Guid ViewId;
    public bool Busy;
    public bool CanEdit;
    public bool CanSpawn;
    public string Message = string.Empty;
}
