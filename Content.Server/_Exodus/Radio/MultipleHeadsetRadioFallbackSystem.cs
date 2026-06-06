using Content.Server.Chat.Systems;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Radio.Components;

namespace Content.Server._Exodus.Radio;

public sealed class MultipleHeadsetRadioFallbackSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly RadioSystem _radio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EntitySpokeEvent>(OnSpeak, after: [typeof(HeadsetSystem)]);
    }

    private void OnSpeak(EntitySpokeEvent args)
    {
        var channel = args.Channel;
        if (channel == null)
            return;

        var enumerator = _inventory.GetSlotEnumerator(args.Source, SlotFlags.WITHOUT_POCKET);
        while (enumerator.NextItem(out var item, out var slot))
        {
            if (!TryComp<HeadsetComponent>(item, out var headset) ||
                !headset.Enabled ||
                (slot.SlotFlags & headset.RequiredSlot) == SlotFlags.NONE ||
                !TryComp<EncryptionKeyHolderComponent>(item, out var keys))
            {
                continue;
            }

            foreach (var entry in keys.Channels)
            {
                if (entry.Channel != channel.ID || !entry.CanSpeak)
                    continue;

                _radio.SendRadioMessage(args.Source, args.Message, channel, item);
                args.Channel = null;
                return;
            }
        }
    }
}
