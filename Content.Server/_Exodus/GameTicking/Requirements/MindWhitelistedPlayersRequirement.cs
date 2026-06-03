using Content.Shared._Exodus.GameTicking.Requirements;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.GameTicking.Requirements;

/// <summary>
/// Game rule requirement: at least <see cref="MinPlayers"/> alive mind-connected players whose
/// mind entity passes <see cref="Whitelist"/>. The whitelist is matched against the mind entity
/// itself (so use <c>mindRoles</c>, <c>components</c>, or <c>tags</c> as they apply to minds —
/// not against the player's body). Re-evaluated on every rule-start attempt.
/// </summary>
public sealed partial class MindWhitelistedPlayersRequirement : GameRuleRequirement
{
    [DataField] public int MinPlayers;

    [DataField(required: true)] public EntityWhitelist Whitelist = new();

    public override bool Check(IEntityManager entity, IPrototypeManager prototype)
    {
        var mobSystem = entity.System<MobStateSystem>();
        var whitelistSystem = entity.System<EntityWhitelistSystem>();

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

            if (!whitelistSystem.IsValid(Whitelist, mindId))
                continue;

            counter++;
        }

        return counter >= MinPlayers;
    }
}
