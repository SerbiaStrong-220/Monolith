using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Genetics;

[Serializable, NetSerializable]
public enum GeneticDiskStatus : byte { Missing, Empty, Ready, Incompatible }

/// <summary>Only stored block values are exposed to players, never the round's mutation assignments.</summary>
[Serializable, NetSerializable]
public sealed class GeneticDiskData(NetEntity? disk, int revision, GeneticDiskStatus status, List<ushort> blocks)
{
    public readonly NetEntity? Disk = disk;
    public readonly int Revision = revision;
    public readonly GeneticDiskStatus Status = status;
    public readonly List<ushort> Blocks = blocks;
}

[Serializable, NetSerializable]
public sealed class GeneticDiskUiState(GeneticDiskData disk) : BoundUserInterfaceState
{
    public readonly GeneticDiskData Disk = disk;
}

[Serializable, NetSerializable]
public enum GeneticPrinterOperation : byte { PrintBlock, PrintGenome, ResetBlock, ClearDisk }

[Serializable, NetSerializable]
public sealed class GeneticPrinterMessage(GeneticPrinterOperation operation, NetEntity disk, int revision, int block)
    : BoundUserInterfaceMessage
{
    public readonly GeneticPrinterOperation Operation = operation;
    public readonly NetEntity Disk = disk;
    public readonly int Revision = revision;
    public readonly int Block = block;
}

[Serializable, NetSerializable]
public sealed class GeneticPrinterUiState(GeneticDiskData disk, bool powered, bool busy, float mutagen)
    : BoundUserInterfaceState
{
    public readonly GeneticDiskData Disk = disk;
    public readonly bool Powered = powered;
    public readonly bool Busy = busy;
    public readonly float Mutagen = mutagen;
}
