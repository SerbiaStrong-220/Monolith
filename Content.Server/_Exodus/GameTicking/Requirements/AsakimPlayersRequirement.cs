using Content.Server.Mind;
using Content.Shared._Exodus.Asakim;
using Content.Shared._Exodus.GameTicking.Requirements;
using Content.Shared.Body.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.GameTicking.Requirements;

/// <summary>
/// Game rule requirement: at least <see cref="MinAsakimPlayers"/> alive mind-connected
/// players must currently have an Asakim brain organ (carrying <see cref="AsakimBrainComponent"/>).
/// Re-evaluated on every rule-start attempt, so the pool resets between attempts naturally.
/// </summary>
public sealed partial class AsakimPlayersRequirement : GameRuleRequirement
{
    [DataField] public int MinAsakimPlayers;

    public override bool Check(IEntityManager entity, IPrototypeManager prototype)
    {
        var mindSystem = entity.System<MindSystem>();
        var mobSystem = entity.System<MobStateSystem>();
        var asakim = entity.System<AsakimIdentitySystem>();

        var counter = 0;

        var query = entity.EntityQueryEnumerator<BodyComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (entity.IsPaused(uid))
                continue;

            if (!mindSystem.TryGetMind(uid, out _, out var mind) || mind.Session == null)
                continue;

            if (mobSystem.IsIncapacitated(uid))
                continue;

            if (!asakim.HasAsakimBrain(uid))
                continue;

            counter++;
        }

        return counter >= MinAsakimPlayers;
    }
}
