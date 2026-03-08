// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Jidort (JunJun)
using Content.Server._Exodus.Speech.Components;
using Content.Server.Speech;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Speech.EntitySystems;

public sealed class BurrinessAccentSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<BurrinessAccentComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, BurrinessAccentComponent component, AccentGetEvent args)
    {
        args.Message = args.Message.Replace("р", "в").Replace("Р", "В");
    }
}
