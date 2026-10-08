using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.BoxSorter;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BoxSorterComponent : Component
{
    public const int MinChannel = 0;
    public const int MaxChannel = 9;

    [DataField, AutoNetworkedField]
    public int? RouteOther;

    [DataField, AutoNetworkedField]
    public Dictionary<string, int> DestinationRoutes = new();

    [DataField]
    public SoundSpecifier TeleportSound = new SoundPathSpecifier("/Audio/Machines/phasein.ogg");

    public static bool IsValidChannel(int channel)
    {
        return channel >= MinChannel && channel <= MaxChannel;
    }
}
