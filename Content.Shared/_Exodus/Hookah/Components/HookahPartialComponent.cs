using Robust.Shared.Audio;

namespace Content.Shared._Exodus.Hookah.Components;

[RegisterComponent]
public sealed partial class HookahPartialComponent : Component
{
    [DataField]
    public SoundSpecifier AssemblySound =
        new SoundPathSpecifier("/Audio/Items/welder.ogg");
}

