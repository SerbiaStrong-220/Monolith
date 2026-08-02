using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Exchanger;

[Serializable, NetSerializable]
public sealed partial class ExchangerDoAfterEvent : DoAfterEvent // Exodus - bluespace RPED target needs custom range checks
{
    // Exodus-begin - bluespace RPED
    [DataField(required: true)]
    public NetEntity ExchangeTarget { get; private set; }

    private ExchangerDoAfterEvent()
    {
    }

    public ExchangerDoAfterEvent(NetEntity exchangeTarget)
    {
        ExchangeTarget = exchangeTarget;
    }

    public override DoAfterEvent Clone() => this;
    // Exodus-end
}
