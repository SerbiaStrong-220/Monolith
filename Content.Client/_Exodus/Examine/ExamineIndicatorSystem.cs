using Content.Client.Examine;
using Robust.Shared.Map;

namespace Content.Client._Exodus.Examine;

public sealed class ExamineIndicatorSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExamineIndicatorComponent, ClientExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<ExamineIndicatorComponent> ent, ref ClientExaminedEvent args)
    {
        if (ent.Comp.ActiveIndicator is { } previous && !TerminatingOrDeleted(previous))
            QueueDel(previous);

        ent.Comp.ActiveIndicator = Spawn(ent.Comp.Indicator, new EntityCoordinates(ent, ent.Comp.Offset));
    }
}
