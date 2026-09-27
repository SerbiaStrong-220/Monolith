using Content.Server.Light.Components;

namespace Content.Server.Light.EntitySystems;

public sealed partial class ExpendableLightSystem
{
    private void OnAutoActivate(Entity<ExpendableLightComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.ActivateOnSpawn)
            TryActivate(ent);
    }
}
