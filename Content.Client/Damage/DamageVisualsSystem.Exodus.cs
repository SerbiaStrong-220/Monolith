// Exodus: suppress humanoid wound overlays while retaining the actual damage and visualizer configuration.
using Content.Client._Exodus.Genetics;
using Content.Shared.Damage;
using Robust.Client.GameObjects;

namespace Content.Client.Damage;

public sealed partial class DamageVisualsSystem
{
    public void RefreshGeneticFormVisuals(Entity<DamageVisualsComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false) || !ent.Comp.Valid ||
            !TryComp<SpriteComponent>(ent, out var sprite) || !TryComp<DamageableComponent>(ent, out var damage))
            return;
        if (HideGeneticFormOverlays(ent.Owner, ent.Comp))
            return;
        if (TryComp<AppearanceComponent>(ent, out var appearance) &&
            AppearanceSystem.TryGetData<bool>(ent, DamageVisualizerKeys.Disabled, out var disabled, appearance))
            ent.Comp.Disabled = disabled;
        if (ent.Comp.Disabled)
            return;
        // Hidden overlays must be redrawn even if the damage threshold did not change during the transformation.
        ent.Comp.LastDamageThreshold = -1;
        foreach (var group in ent.Comp.LastThresholdPerGroup.Keys)
            ent.Comp.LastThresholdPerGroup[group] = -1;
        ForceUpdateLayers(damage, sprite, ent.Comp);
    }

    private bool HideGeneticFormOverlays(EntityUid uid, DamageVisualsComponent visuals)
    {
        if (!visuals.Overlay || !GeneticFormPresentationSystem.IsAlternateForm(EntityManager, uid))
            return false;
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return true;
        if (visuals.TargetLayers != null)
        {
            foreach (var layer in visuals.TargetLayerMapKeys)
            {
                if (visuals.DamageOverlayGroups != null)
                {
                    foreach (var group in visuals.DamageOverlayGroups.Keys)
                        HideOverlay(sprite, $"{layer}{group}");
                }
                else
                    HideOverlay(sprite, $"{layer}trackDamage");
            }
        }
        else if (visuals.DamageOverlayGroups != null)
        {
            foreach (var group in visuals.DamageOverlayGroups.Keys)
                HideOverlay(sprite, $"DamageOverlay{group}");
        }
        else
            HideOverlay(sprite, "DamageOverlay");
        return true;
    }

    private static void HideOverlay(SpriteComponent sprite, string key)
    {
        if (sprite.LayerMapTryGet(key, out var layer))
            sprite.LayerSetVisible(layer, false);
    }
}
