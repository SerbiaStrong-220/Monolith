using Robust.Shared.Audio;

namespace Content.Server._Exodus.Implants;

[RegisterComponent]
public sealed partial class TransferSolutionOnTriggerComponent : Component
{
    [DataField("solutions")]
    public List<TransferSolutionEntry> Solutions = [];

    [DataField]
    public SoundSpecifier InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");
}

[Serializable]
[DataRecord]
public sealed partial class TransferSolutionEntry()
{
    public string Name = "";
    public int Charges = 1;
    public float TransferAmount = 10.0f;
    public int UsedCount = 0;
}
