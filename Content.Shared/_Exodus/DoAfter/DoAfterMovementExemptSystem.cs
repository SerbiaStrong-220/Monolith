namespace Content.Shared._Exodus.DoAfter;

public sealed class DoAfterMovementExemptSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DoAfterMovementExemptComponent, GetDoAfterMovementBreakEvent>(OnGetDoAfterMovementBreak);
    }

    private void OnGetDoAfterMovementBreak(Entity<DoAfterMovementExemptComponent> ent,
        ref GetDoAfterMovementBreakEvent args)
    {
        args.BreakOnMove = false;
    }
}

[ByRefEvent]
public record struct GetDoAfterMovementBreakEvent(bool BreakOnMove);
