using Content.Shared._Goobstation.Flashbang;

namespace Content.Shared.Exodus.Gimmicks.SensitiveEyes;

public sealed partial class SensitiveEyesSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SensitiveEyesComponent, FlashDurationMultiplierEvent>(OnFlashModifiersEvent);
    }

    private void OnFlashModifiersEvent(Entity<SensitiveEyesComponent> entity, ref FlashDurationMultiplierEvent args)
    {
        args.Multiplier *= entity.Comp.DurationModifier;
    }
}
