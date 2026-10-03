// Exodus: genetic claws can be added and removed without leaving accuracy or attack-mode penalties behind.
using Content.Shared._DV.Weapons.Ranged.Components;
using Content.Shared._Mono.Claws.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._Mono.Claws;

public abstract partial class SharedClawsSystem
{
    [Dependency] private INetManager _net = default!;

    private void InitializeClawLifecycle()
    {
        SubscribeLocalEvent<ClawsComponent, ComponentStartup>(OnClawsStartup);
        SubscribeLocalEvent<ClawsComponent, AfterAutoHandleStateEvent>(OnClawsState);
        SubscribeLocalEvent<ClawsComponent, ComponentShutdown>(OnClawsShutdown);
    }

    private void OnClawsStartup(Entity<ClawsComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<MeleeWeaponComponent>(ent, out var melee))
        {
            ent.Comp.OriginalWideSwing = melee.CanWideSwing;
            ent.Comp.OriginalAltDisarm = melee.AltDisarm;
            ent.Comp.CapturedMelee = true;
        }
        ent.Comp.OriginalSpread = TryComp<PlayerAccuracyModifierComponent>(ent, out var accuracy) ? accuracy.SpreadMultiplier : null;
        UpdateClaws(ent, ent.Comp);
    }

    private void OnClawsState(Entity<ClawsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateClaws(ent, ent.Comp);
    }

    private void OnClawsShutdown(Entity<ClawsComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;
        if (ent.Comp.CapturedMelee && TryComp<MeleeWeaponComponent>(ent, out var melee))
        {
            melee.CanWideSwing = ent.Comp.OriginalWideSwing;
            melee.AltDisarm = ent.Comp.OriginalAltDisarm;
            Dirty(ent, melee);
        }
        if (!TryComp<PlayerAccuracyModifierComponent>(ent, out var accuracy) || !ReferenceEquals(accuracy, ent.Comp.AppliedAccuracy))
            return;
        if (ent.Comp.OriginalSpread is { } spread)
        {
            accuracy.SpreadMultiplier = spread;
            Dirty(ent, accuracy);
        }
        // The client's state application already queues this networked component for removal.
        // Only the server may remove it here, otherwise that queue tries deleting it a second time.
        else if (_net.IsServer)
            RemComp(ent, accuracy);
    }
}
