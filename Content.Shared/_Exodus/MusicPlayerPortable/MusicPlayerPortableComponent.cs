using Robust.Shared.GameStates;
namespace Content.Shared._Exodus.MusicPlayerPortable;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MusicPlayerPortableComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan NextUpdate;

    [DataField, AutoNetworkedField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField]
    public float DrawRate = 0.05f;

}
