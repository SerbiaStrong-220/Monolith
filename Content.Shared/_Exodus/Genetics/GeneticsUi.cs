using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Genetics;

[Serializable, NetSerializable]
public enum GeneticsUiKey : byte { Laboratory, RemoteViewing, Disk, Printer }

[Serializable, NetSerializable]
public enum GeneticsOperation : byte
{
    Scan, Edit, SetBlock, Reset, StoreBuffer, RestoreBuffer, WriteDisk, ReadDisk, PrintInjector,
    PrintGenome, PrintBufferInjector, PrintBufferGenome,
    EjectPatient, EjectDisk,
}

[Serializable, NetSerializable]
public sealed class GeneticsMessage(GeneticsOperation operation, NetEntity? patient, int revision, int block = 0, int value = 0, int buffer = 0, int digit = 0)
    : BoundUserInterfaceMessage
{
    public readonly GeneticsOperation Operation = operation;
    public readonly NetEntity? Patient = patient;
    public readonly int Revision = revision;
    public readonly int Block = block;
    public readonly int Value = value;
    public readonly int Buffer = buffer;
    public readonly int Digit = digit;
}

[Serializable, NetSerializable]
public sealed class GeneticBlockInfo(ushort value, string? name = null, string? description = null, bool? active = null)
{
    public readonly ushort Value = value;
    public readonly string? Name = name;
    public readonly string? Description = description;
    public readonly bool? Active = active;
}

[Serializable, NetSerializable]
public sealed class GeneticsUiState(NetEntity? patientEntity, string patient, int revision, int? stability, bool powered, bool busy,
    float mutagen, List<GeneticBlockInfo> blocks, bool[] buffers, bool disk, bool debug, bool living) : BoundUserInterfaceState
{
    public readonly string Patient = patient;
    public readonly NetEntity? PatientEntity = patientEntity;
    public readonly int Revision = revision;
    public readonly int? Stability = stability;
    public readonly bool Debug = debug;
    public readonly bool Living = living;
    public readonly bool Powered = powered;
    public readonly bool Busy = busy;
    public readonly float Mutagen = mutagen;
    public readonly List<GeneticBlockInfo> Blocks = blocks;
    public readonly bool[] Buffers = buffers;
    public readonly bool Disk = disk;
}

[Serializable, NetSerializable]
public sealed class GeneticViewMessage(NetEntity? target, bool refresh = false) : BoundUserInterfaceMessage
{
    public readonly NetEntity? Target = target;
    public readonly bool Refresh = refresh;
}

[Serializable, NetSerializable]
public sealed class GeneticViewState(Dictionary<NetEntity, string> targets, NetEntity? eye) : BoundUserInterfaceState
{
    public readonly Dictionary<NetEntity, string> Targets = targets;
    public readonly NetEntity? Eye = eye;
}
