using Content.Client.Examine;
using Content.Shared._Exodus.Nebula;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Nebula;

/// <summary>
/// Shows the nebula gas siphon's output port when the machine is examined.
/// </summary>
public sealed class NebulaGasSiphonSystem : EntitySystem
{
    private static readonly EntProtoId ArrowPrototype = "NebulaGasSiphonArrow";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NebulaGasSiphonComponent, ClientExaminedEvent>(OnSiphonExamined);
    }

    private void OnSiphonExamined(Entity<NebulaGasSiphonComponent> entity, ref ClientExaminedEvent args)
    {
        Spawn(ArrowPrototype, new EntityCoordinates(entity.Owner, entity.Comp.PipeArrowPosition));
    }
}
