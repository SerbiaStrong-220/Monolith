using Content.Server.Popups;
using Content.Server.Research.Disk;
using Content.Server.Research.Systems;
using Content.Shared.Research.Components;
using Content.Shared.Research.Systems;
using Content.Shared.Research.TechnologyDisk.Components;

namespace Content.Server._Exodus.Research;

/// <summary>
/// Handles the research point and technology disk integrations for the R&amp;D server magnet.
/// Additional media types can handle <see cref="ResearchServerMagnetInsertAttemptEvent"/> independently.
/// </summary>
public sealed class ResearchServerDiskMagnetIntegrationSystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private SharedResearchSystem _sharedResearch = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ResearchDiskComponent, ResearchServerMagnetInsertAttemptEvent>(OnResearchDiskInsertAttempt);
        SubscribeLocalEvent<TechnologyDiskComponent, ResearchServerMagnetInsertAttemptEvent>(OnTechnologyDiskInsertAttempt);
    }

    private void OnResearchDiskInsertAttempt(Entity<ResearchDiskComponent> ent,
        ref ResearchServerMagnetInsertAttemptEvent args)
    {
        if (args.Handled || TerminatingOrDeleted(ent) || TerminatingOrDeleted(args.Server))
            return;

        if (!TryQueueDel(ent))
            return;

        _research.ModifyServerPoints(args.Server, ent.Comp.Points, args.Server.Comp);
        _popup.PopupEntity(Loc.GetString("research-server-disk-magnet-inserted-points", ("points", ent.Comp.Points)),
            args.Server);
        args.Handled = true;
    }

    private void OnTechnologyDiskInsertAttempt(Entity<TechnologyDiskComponent> ent,
        ref ResearchServerMagnetInsertAttemptEvent args)
    {
        if (args.Handled || TerminatingOrDeleted(ent) || TerminatingOrDeleted(args.Server) ||
            !TryComp<TechnologyDatabaseComponent>(args.Server, out var database))
        {
            return;
        }

        if (!TryQueueDel(ent))
            return;

        if (ent.Comp.Recipes != null)
        {
            foreach (var recipe in ent.Comp.Recipes)
            {
                _sharedResearch.AddLatheRecipe(args.Server, recipe, database);
            }
        }

        _popup.PopupEntity(Loc.GetString("research-server-disk-magnet-inserted-technology"), args.Server);
        args.Handled = true;
    }
}
