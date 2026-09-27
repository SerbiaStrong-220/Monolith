using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Genetics;

[Serializable, NetSerializable]
public sealed class GeneticsAdminState(string target, string context, int revision, int stability, List<GeneticBlockInfo> blocks)
    : EuiStateBase
{
    public readonly string Target = target;
    public readonly string Context = context;
    public readonly int Revision = revision;
    public readonly int Stability = stability;
    public readonly List<GeneticBlockInfo> Blocks = blocks;
}

[Serializable, NetSerializable]
public sealed class GeneticsAdminSetBlockMessage(string context, int revision, int block, bool enabled) : EuiMessageBase
{
    public readonly string Context = context;
    public readonly int Revision = revision;
    public readonly int Block = block;
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class GeneticsAdminRefreshMessage : EuiMessageBase;
