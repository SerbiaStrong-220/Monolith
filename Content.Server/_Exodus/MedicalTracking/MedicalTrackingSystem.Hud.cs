using Content.Shared._Exodus.MedicalTracking;

namespace Content.Server._Exodus.MedicalTracking;

public sealed partial class MedicalTrackingSystem
{
    private void UpdateHudBorder(Entity<MedicalTrackingImplantComponent> implant, EntityUid body)
    {
        if (implant.Comp.HudBorder == null)
        {
            RemCompDeferred<MedicalTrackingHudComponent>(body);
            return;
        }

        var hud = EnsureComp<MedicalTrackingHudComponent>(body);
        hud.Border = implant.Comp.HudBorder;
        Dirty(body, hud);
    }
}
