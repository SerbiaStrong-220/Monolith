using Content.Shared.Chemistry.Components;
using Content.Shared.Gibbing.Events;

namespace Content.Shared._Exodus.Chemistry;

/// <summary>Internal solution entities remain with their container when physical contents are gibbed.</summary>
public sealed partial class SolutionGibbingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SolutionComponent, AttemptEntityGibEvent>(OnGib);
    }

    private void OnGib(Entity<SolutionComponent> ent, ref AttemptEntityGibEvent args)
    {
        args.GibType = GibType.Skip;
    }
}
