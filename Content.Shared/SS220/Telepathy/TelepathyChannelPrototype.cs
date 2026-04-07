// (c) Space Exodus Team - EXDS-RL with CLA

using Content.Shared.SS220.TTS;
using Robust.Shared.Prototypes;

namespace Content.Shared.SS220.Telepathy;

[Prototype("telepathyChannel")]
public sealed partial class TelepathyChannelPrototype : IHearableChannelPrototype
{
    [IdDataField, ViewVariables]
    public string ID { get; private set; } = default!;

    [DataField]
    public ChannelParameters ChannelParameters = new();

    public string LocalizedName => Loc.GetString(ChannelParameters.Name);

    public Color Color => ChannelParameters.Color;
}

[DataDefinition]
public sealed partial class ChannelParameters()
{
    [DataField]
    public string Name { get; private set; } = string.Empty;

    [DataField]
    public Color Color { get; private set; } = Color.Lime;

    public ChannelParameters(string name, Color color) : this()
    {
        Name = name;
        Color = color;
    }
}
