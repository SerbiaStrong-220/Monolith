using Content.Shared._Exodus.Asakim;
using Content.Shared._Exodus.GameTicking.Requirements;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.GameTicking.Requirements;

/// <summary>
/// Game rule requirement: at least <see cref="MinAsakimPlayers"/> alive mind-connected
/// players must have an Asakim mind role.
/// Re-evaluated on every rule-start attempt, so the pool resets between attempts naturally.
/// </summary>
public sealed partial class AsakimPlayersRequirement : GameRuleRequirement
{
    [DataField] public int MinAsakimPlayers;

    public override bool Check(IEntityManager entity, IPrototypeManager prototype)
    {
        var mobSystem = entity.System<MobStateSystem>();
        var roleSystem = entity.System<SharedRoleSystem>();

        var counter = 0;

        var query = entity.EntityQueryEnumerator<MindComponent>();
        while (query.MoveNext(out var mindId, out var mind))
        {
            if (mind.Session == null || mind.CurrentEntity is not { } uid)
                continue;

            if (!entity.EntityExists(uid) || entity.IsPaused(uid))
                continue;

            if (mobSystem.IsIncapacitated(uid))
                continue;

            if (!roleSystem.MindHasRole<AsakimRoleComponent>((mindId, mind), out _))
                continue;

            counter++;
        }

        return counter >= MinAsakimPlayers;
    }
}
