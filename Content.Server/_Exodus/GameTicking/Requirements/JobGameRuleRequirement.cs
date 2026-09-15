// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Shared._Exodus.GameTicking.Requirements;
using Content.Server._NF.CryoSleep;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;

namespace Content.Server._Exodus.GameTicking.Requirements;

public sealed partial class JobGameRuleRequirement : GameRuleRequirement
{
    // TODO: these should be splitten into two separte classes but currently are not
    [DataField] public ProtoId<JobPrototype>? Job;
    [DataField] public ProtoId<DepartmentPrototype>? Department;
    [DataField] public int MinJobPlayers = 0;
    [DataField] public int MinDepartmentPlayers = 0;

    public override bool Check(IEntityManager entity, IPrototypeManager prototype)
    {
        if (!prototype.TryIndex(Department, out var department))
            return true;

        var mobSystem = entity.System<MobStateSystem>();

        var jobCounter = 0;
        var departmentCounter = 0;

        var jobQuery = entity.GetEntityQuery<PlayerJobComponent>();
        var departmentQuery = entity.GetEntityQuery<GameRuleDepartmentMemberComponent>();
        var query = entity.EntityQueryEnumerator<ActorComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            if (entity.IsPaused(uid))
                continue;

            // mob checks
            if (mobSystem.IsIncapacitated(uid))
                continue;

            var job = jobQuery.TryGetComponent(uid, out var player) ? player.JobPrototype : null;

            if (job != null && job == Job)
                jobCounter++;

            // A job and explicit membership must not count the same player twice.
            if (job != null && department.Roles.Contains(job.Value)
                || departmentQuery.TryGetComponent(uid, out var membership)
                && membership.Departments.Contains(department.ID))
                departmentCounter++;
        }

        return jobCounter >= MinJobPlayers && departmentCounter >= MinDepartmentPlayers;
    }
}
