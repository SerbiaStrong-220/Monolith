using Content.Shared.Sound.Components;
using Content.Server.Body.Components;


using Robust.Shared.Audio.Systems;
using Content.Server.Body.Systems;

namespace Content.Shared.Sound.Systems;

public sealed partial class InternalsNoiseSystem : EntitySystem // Exodus: generated dependency injection.
{

    [Dependency] private InternalsSystem _internals = default!; // Exodus: generated dependency injection.

    [Dependency] private SharedAudioSystem _audio = default!; // Exodus: generated dependency injection.


    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmitSoundOnInternalsActiveComponent, InhaleLocationEvent>(OnInhaleLocation);
    }

    public void OnInhaleLocation(Entity<EmitSoundOnInternalsActiveComponent> ent, ref InhaleLocationEvent args)
    {
        if (_internals.AreInternalsWorking(ent))
            _audio.PlayPvs(ent.Comp.Sound, ent.Owner);

    }

}