// Exodus: change a lung's breathing alert without replacing its air or solutions.
using Content.Shared.Alert;
using Content.Shared.Body.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.Body.Systems;

public sealed partial class LungSystem
{
    public void SetBreathingAlert(Entity<LungComponent> ent, ProtoId<AlertPrototype> alert)
    {
        ent.Comp.Alert = alert;
    }
}
