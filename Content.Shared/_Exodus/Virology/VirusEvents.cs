// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Inventory;

namespace Content.Shared._Exodus.Virology;

[ByRefEvent]
public readonly record struct VirusContentsChangedEvent;

[ByRefEvent]
public readonly record struct VirusDoseAbsorbedEvent(VirusSymptomState Symptom);

[ByRefEvent]
public record struct VirusAddAttempt(EntityUid Target, VirusTransmissionVector Vector, float BlockChance = 0f) : IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}
