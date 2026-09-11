using Content.Shared.NameModifier.EntitySystems;
using Robust.Shared.Network;

namespace Content.Shared._Exodus.NameModifier;

public sealed partial class EntityNamePrefixSystem : EntitySystem
{
    [Dependency] private NameModifierSystem _names = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntityNamePrefixComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<EntityNamePrefixComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<EntityNamePrefixComponent, RefreshNameModifiersEvent>(OnRefresh);
    }

    private void OnStartup(Entity<EntityNamePrefixComponent> ent, ref ComponentStartup args)
    {
        // The server replicates the composed name. Clients may not have received the base name yet.
        if (_net.IsServer)
            _names.RefreshNameModifiers(ent.Owner);
    }

    private void OnShutdown(Entity<EntityNamePrefixComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsServer && !TerminatingOrDeleted(ent.Owner))
            _names.RefreshNameModifiers(ent.Owner);
    }

    private void OnRefresh(Entity<EntityNamePrefixComponent> ent, ref RefreshNameModifiersEvent args)
    {
        if (ent.Comp.LifeStage >= ComponentLifeStage.Stopping || string.IsNullOrEmpty(ent.Comp.Prefix))
            return;

        args.AddModifier("entity-name-prefix", extraArgs: [("prefix", Loc.GetString(ent.Comp.Prefix))]);
    }
}
