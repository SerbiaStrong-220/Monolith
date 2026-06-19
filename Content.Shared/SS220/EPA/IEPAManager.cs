// (c) Space Exodus Team - EXDS-RL with CLA

using System.Threading.Tasks;
using Robust.Shared.Network;

namespace Content.Shared.SS220.EPA;

/// <summary>
/// External Personal Account Manager - manager for
/// working with API of external service storing users data
/// </summary>
public interface IEPAManager
{
    void Initialize();
}

/// <inheritdoc />
public interface IClientEPAManager : IEPAManager
{
    string? AuthUrl { get; }
    /// <summary>
    /// Sends a request to the server to check is auth completed
    /// </summary>
    void CheckAuthState();
}

public interface IServerEPAManager : IEPAManager
{
    /// <summary>
    /// Invoked when EPA authorization completed before handshake release
    /// </summary>
    event Func<INetChannel, Task> AuthFinished;
}
