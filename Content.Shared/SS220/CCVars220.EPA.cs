// (c) Space Exodus Team - EXDS-RL with CLA

using Robust.Shared.Configuration;

namespace Content.Shared.SS220.CCVars;

public sealed partial class CCVars220
{
    /// <summary>
    /// Mode for <see cref="EPA.IEPAManager"/>, see <see cref="EPA.EPAMode"/>
    /// </summary>
    public static readonly CVarDef<int> EPAMode =
        CVarDef.Create("epa.mode", 0, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Base64 encoded JWT key used for validation
    /// </summary>
    public static readonly CVarDef<string> EPAJwtDerKey =
        CVarDef.Create("epa.jwt_der_key", "", CVar.SERVERONLY);

    /// <summary>
    /// Path to PEM-encoded file with JWT key used for validation
    /// </summary>
    public static readonly CVarDef<string> EPAJwtPemKeyPath =
        CVarDef.Create("epa.jwt_pem_key_path", "", CVar.SERVERONLY);

    /// <summary>
    /// EPA JWT issuer
    /// </summary>
    public static readonly CVarDef<string> EPAJwtIssuer =
        CVarDef.Create("epa.jwt_iss", "", CVar.SERVERONLY);

    /// <summary>
    /// EPA JWT audience
    /// </summary>
    public static readonly CVarDef<string> EPAJwtAudience =
        CVarDef.Create("epa.jwt_aud", "", CVar.SERVERONLY);

    /// <summary>
    /// How much clock skew is allowed in seconds, default is 5 minutes
    /// </summary>
    public static readonly CVarDef<int> EPAJwtClockSkew =
        CVarDef.Create("epa.jwt_clock_skew", 300, CVar.SERVERONLY);

    /// <summary>
    /// Base address used for API
    /// </summary>
    public static readonly CVarDef<string> EPAApiUrl =
        CVarDef.Create("epa.api_url", "", CVar.SERVERONLY);

    /// <summary>
    /// API key used by EPA
    /// </summary>
    public static readonly CVarDef<string> EPAApiKey =
        CVarDef.Create("epa.api_key", "", CVar.SERVERONLY);
}
