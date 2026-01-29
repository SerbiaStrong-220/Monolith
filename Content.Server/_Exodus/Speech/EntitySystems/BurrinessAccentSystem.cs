using Content.Server.Exodus.Speech.Components;
using Robust.Shared.Random;

namespace Content.Server.Speech.EntitySystems
{
    public sealed class BurrinessAccentSystem : EntitySystem
    {
        [Dependency] private readonly IRobustRandom _random = default!;

        public override void Initialize()
        {
            SubscribeLocalEvent<BurrinessAccentComponent, AccentGetEvent>(OnAccent);
        }

        private void OnAccent(EntityUid uid, BurrinessAccentComponent component, AccentGetEvent args)
        {
            args.Message = args.Message.Replace("р", "в").Replace("Р", "В");
        }
    }
}
