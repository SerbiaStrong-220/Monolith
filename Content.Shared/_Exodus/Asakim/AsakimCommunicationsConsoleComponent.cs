namespace Content.Shared._Exodus.Asakim;

[RegisterComponent]
public sealed partial class AsakimCommunicationsConsoleComponent : Component
{
    [DataField]
    public LocId SenderFallback = "asakim-communications-console-sender-fallback";
}
