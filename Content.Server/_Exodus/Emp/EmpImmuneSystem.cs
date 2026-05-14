using Content.Server.Emp;

namespace Content.Server._Exodus.Emp;

public sealed class EmpImmuneSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<EmpImmuneComponent, EmpAttemptEvent>(OnEmpAttempt);
    }

    private void OnEmpAttempt(Entity<EmpImmuneComponent> ent, ref EmpAttemptEvent args)
    {
        args.Cancel();
    }
}
