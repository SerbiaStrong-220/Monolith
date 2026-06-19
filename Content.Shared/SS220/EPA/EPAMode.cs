// (c) Space Exodus Team - EXDS-RL with CLA

namespace Content.Shared.SS220.EPA;

/// <summary>
/// Mode for <see cref="IEPAManager"/>
/// </summary>
public enum EPAMode : int
{
    /// <summary>
    /// EPA disabled
    /// </summary>
    Disabled = 0,
    /// <summary>
    /// Intercepts handshake and enforces client to authorize with EPA but doesn't acquires information from external sources
    /// </summary>
    Validation = 1,
    /// <summary>
    /// Enables full authorization mechanism on client connection during handshake.
    /// Usually should go with <see cref="Robust.Shared.Network.AuthMode.Disabled"/>.
    /// </summary>
    Authorization = 2,
}
