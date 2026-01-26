using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared.Exodus.Gimmicks.Pheromones.UI;

[Serializable, NetSerializable]
public sealed partial class PheromonesAskEuiConfirmMessage : EuiMessageBase
{
    public string Text;

    public PheromonesAskEuiConfirmMessage(string text)
    {
        Text = text;
    }
}
