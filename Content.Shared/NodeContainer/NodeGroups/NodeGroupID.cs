namespace Content.Shared.NodeContainer.NodeGroups;

public enum NodeGroupID : byte
{
    Default,
    HVPower,
    MVPower,
    Apc,
    AMEngine,
    Pipe,
    WireNet,

    /// <summary>
    /// Group used by the TEG.
    /// </summary>
    /// <seealso cref="Content.Server.Power.Generation.Teg.TegSystem"/>
    /// <seealso cref="Content.Server.Power.Generation.Teg.TegNodeGroup"/>
    Teg,

    // Exodus-begin
    /// <summary>
    /// Ship mining material pipes (ore duct under machines).
    /// </summary>
    ExodusMiningPipe,

    /// <summary>
    /// Gas pipes with armored fittings, isolated from ordinary atmospheric plumbing.
    /// </summary>
    ExodusArmoredGasPipe,
    // Exodus-end
}
