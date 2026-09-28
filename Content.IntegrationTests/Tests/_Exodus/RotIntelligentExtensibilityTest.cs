using Content.Server._Exodus.Virology.Intelligent;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Server.NPC.HTN;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Examine;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task RemoteVisionRespectsTargetRestrictionsAndConfiguredSources()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid target = default;
        await server.WaitAssertion(() =>
        {
            core = em.SpawnEntity("MobRotIntelligent", map.GridCoords);
            target = em.SpawnEntity("MobRotNester", map.GridCoords);
            em.RemoveComponent<HTNComponent>(target);
            Assert.That(em.HasComponent<RotSatedComponent>(target), Is.False,
                "A defender must not require the sated creature's digestion or reproductive state.");
            Assert.That(em.HasComponent<RotDefenderComponent>(target), Is.True);
            var member = em.GetComponent<RotColonyMemberComponent>(target);
            member.VisionRange = 3;
            Assert.That(em.System<RotIntelligentSystem>().Join(target, core), Is.True);
            Assert.That(member.VisionRange, Is.EqualTo(3), "Joining must preserve prototype-specific vision.");
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var examine = em.System<ExamineSystemShared>();
            Assert.That(examine.CanExamine(core, target), Is.True);
            em.AddComponent<RotAuditExamineBlockerComponent>(target);
            Assert.That(examine.CanExamine(core, target), Is.False,
                "Remote vision must still raise the target's cancellable ExamineAttemptEvent.");
            em.RemoveComponent<RotAuditExamineBlockerComponent>(target);
            Assert.That(examine.CanExamine(core, target), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    public sealed class RotAuditExamineSystem : EntitySystem
    {
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<RotAuditExamineBlockerComponent, ExamineAttemptEvent>(OnExamine);
        }

        private void OnExamine(Entity<RotAuditExamineBlockerComponent> ent, ref ExamineAttemptEvent args)
        {
            args.Cancel();
        }
    }
}

[RegisterComponent]
public sealed partial class RotAuditExamineBlockerComponent : Component;
