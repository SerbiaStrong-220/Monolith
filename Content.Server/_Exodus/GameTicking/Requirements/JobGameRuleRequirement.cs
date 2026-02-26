// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Shared.Exodus.GameTicking.Requirements;
using Content.Server._NF.CryoSleep;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Content.Server.Mind;
using Content.Shared.Mobs.Systems;

namespace Content.Server.Exodus.GameTicking.Requirements;

public sealed partial class JobGameRuleRequirement : GameRuleRequirement
{
    // TODO: these should be splitten into two separte classes but currently are not
    [DataField] public ProtoId<JobPrototype> Job;
    [DataField] public ProtoId<DepartmentPrototype> Department;
    [DataField] public int MinJobPlayers = 0;
    [DataField] public int MinDepartmentPlayers = 0;

    public override bool Check(IEntityManager entity, IPrototypeManager prototype)
    {
        if (!prototype.TryIndex(Department, out var department))
            return true;

        var mindSystem = entity.System<MindSystem>();
        var mobSystem = entity.System<MobStateSystem>();

        var players = entity.EntityQueryEnumerator<PlayerJobComponent>();
        var jobCounter = 0;
        var departmentCounter = 0;

        while (players.MoveNext(out var uid, out var player))
        {
            if (player.JobPrototype == null)
                continue;

            if (entity.IsPaused(uid))
                continue;

            // mob checks
            if (!mindSystem.TryGetMind(uid, out _, out var mind) || mind.Session == null)
                continue;

            if (mobSystem.IsIncapacitated(uid))
                continue;

            // update counters
            if (player.JobPrototype == Job)
                jobCounter++;

            if (department.Roles.Contains(player.JobPrototype.Value))
                departmentCounter++;
        }

        return jobCounter >= MinJobPlayers && departmentCounter >= MinDepartmentPlayers;
    }
}
