using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public partial class XCVars
{
    public static readonly CVarDef<int> EmergencyShuttleCountdown =
        CVarDef.Create("shuttle.auto_call_countdown", 25, CVar.SERVERONLY);
}
