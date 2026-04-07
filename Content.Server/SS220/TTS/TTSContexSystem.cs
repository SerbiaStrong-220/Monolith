// (c) Space Exodus Team - EXDS-RL with CLA

using System.Diagnostics.CodeAnalysis;
using Content.Server.VoiceMask;
using Content.Shared.Inventory;
using Content.Shared.SS220.TTS;
using Robust.Shared.Prototypes;

namespace Content.Server.SS220.TTS;

public sealed partial class TTSContextSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public bool TryGetVoiceID(EntityUid uid, [NotNullWhen(true)] out ProtoId<TTSVoicePrototype>? voiceId)
    {
        voiceId = null;
        if (!TryComp(uid, out TTSComponent? senderComponent))
            return false;

        voiceId = senderComponent.VoicePrototypeId;

        if (TryGetVoiceMaskUid(uid, out var maskUid))
        {
            var voiceEv = new TransformSpeakerVoiceEvent(maskUid.Value, voiceId);
            RaiseLocalEvent(maskUid.Value, ref voiceEv);
            voiceId = voiceEv.VoiceId;
        }

        return voiceId is not null;
    }

    public bool TryGetVoiceMaskUid(EntityUid maskCarrier, [NotNullWhen(true)] out EntityUid? maskUid)
    {
        maskUid = null;
        if (!_inventory.TryGetContainerSlotEnumerator(maskCarrier, out var carrierSlot, SlotFlags.MASK))
            return false;

        while (carrierSlot.NextItem(out var itemUid, out var itemSlot))
        {
            if (HasComp<VoiceMaskComponent>(itemUid))
            {
                maskUid = itemUid;
                return true;
            }
        }
        return false;
    }
}

