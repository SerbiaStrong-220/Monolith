using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.BoxSorter;

[RegisterComponent, NetworkedComponent]
public sealed partial class BoxSorterComponent : Component
{
    public const int MinChannel = 0;
    public const int MaxChannel = 9;

    [DataField]
    public int? RouteOther;

    [DataField]
    public Dictionary<EntityUid, int> DestinationRoutes = new();

    [DataField]
    public SoundSpecifier TeleportSound = new SoundPathSpecifier("/Audio/Machines/phasein.ogg");
}
