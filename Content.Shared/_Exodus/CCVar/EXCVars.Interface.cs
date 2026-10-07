using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public sealed partial class EXCVars
{
    /// <summary>
    /// Status icon theme prototype used for alerts and the body damage indicator.
    /// </summary>
    public static readonly CVarDef<string> StatusIconTheme =
        CVarDef.Create("exds.status_icon_theme", "Monolith", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Gradually reveals local speech and whispers in speech bubbles.
    /// </summary>
    public static readonly CVarDef<bool> ChatTypewriterEnabled =
        CVarDef.Create("exds.chat_typewriter_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
