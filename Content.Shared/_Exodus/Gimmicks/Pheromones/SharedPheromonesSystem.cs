namespace Content.Shared.Exodus.Gimmicks.Pheromones;

public abstract partial class SharedPheromonesSystem : EntitySystem
{
    public bool CanSeePheromones(EntityUid? entity)
    {
        if (entity == null)
            return false;

        return HasComp<PheromonesCommunicationComponent>(entity.Value);
    }
}
