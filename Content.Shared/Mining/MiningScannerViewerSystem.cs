// Exodus-MiningScannerRefactor
using System.Linq;
using Content.Shared.Mining.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Shared.Mining;

public sealed partial class MiningScannerViewerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MiningScannerViewerComponent, ComponentGetState>(GetState);
        SubscribeLocalEvent<MiningScannerViewerComponent, ComponentHandleState>(HandleState);
    }

    private void GetState(EntityUid uid, MiningScannerViewerComponent comp, ref ComponentGetState args)
    {
        args.State = new MiningScannerViewerComponentState()
        {
            Records = comp.Records,
        };
    }

    private void HandleState(EntityUid uid, MiningScannerViewerComponent comp, ref ComponentHandleState args)
    {
        if (args.Current is not MiningScannerViewerComponentState state)
            return;

        comp.Records = state.Records;
    }

    public void CreateScan(EntityUid uid, float range, TimeSpan delay, float animationDuration = 1.5f)
    {
        var scan = new MiningScannerRecord()
        {
            AnimationDuration = TimeSpan.FromSeconds(animationDuration),
            ViewRange = range,
            CreatedAt = _timing.CurTime,
            PingLocation = _transform.GetMapCoordinates(uid),
            Delay = delay,
        };

        var viewer = EnsureComp<MiningScannerViewerComponent>(uid);
        viewer.Records.Add(scan);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // when scanner records is out of date it would be better to clean redunant data
        // when we don't have any usefull data in component delete it fully

        var viewers = EntityQueryEnumerator<MiningScannerViewerComponent>();

        while (viewers.MoveNext(out var uid, out var viewer))
        {
            var records = viewer.Records.Where(record => record.CreatedAt + record.Delay + record.AnimationDuration > _timing.CurTime).ToList();

            if (records.Count == 0)
            {
                RemCompDeferred(uid, viewer);
                continue;
            }

            if (records.Count != viewer.Records.Count)
            {
                viewer.Records = records;
            }
        }
    }
}
