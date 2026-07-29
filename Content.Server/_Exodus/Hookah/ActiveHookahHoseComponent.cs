using System;

namespace Content.Server._Exodus.Hookah;

[RegisterComponent]
public sealed partial class ActiveHookahHoseComponent : Component
{
    public TimeSpan Accum;
}

