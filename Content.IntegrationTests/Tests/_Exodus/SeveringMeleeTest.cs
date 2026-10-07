using System.Linq;
using System.Numerics;
using Content.Server._Exodus.Projectiles;
using Content.Server.Silicons.Borgs;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Destructible;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(SeveringProjectileSystem))]
public sealed class SeveringMeleeTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusSeveringTestBlade
          parent: CombatKnife
          components:
          - type: MeleeWeapon
            damage:
              types:
                Slash: 1
          - type: SeveringProjectile
            chance: 1
            parts: [Head, Arm, Leg]

        - type: entity
          id: ExodusSeveringTestProjectile
          parent: BaseBullet
          components:
          - type: Projectile
            damage:
              types:
                Slash: 1
          - type: SeveringProjectile

        - type: damageModifierSet
          id: ExodusSeveringTestImmune
          coefficients:
            Slash: 0

        - type: entity
          id: ExodusSeveringTestArmoredHuman
          parent: MobHuman
          components:
          - type: Damageable
            damageModifierSet: ExodusSeveringTestImmune

        - type: entity
          id: ExodusSeveringTestObject
          components:
          - type: Damageable
          - type: SeveringMeleeTestReceiver
          - type: Destructible
            thresholds:
            - trigger: !type:DamageTrigger
                damage: 1
              behaviors:
              - !type:DoActsBehavior
                acts: [Destruction]
        """;

    [TestCase(false, 0f, 5)]
    [TestCase(false, 1f, 4)]
    [TestCase(true, 0f, 5)]
    [TestCase(true, 1f, 4)]
    public async Task MeleeHitsRespectChanceAndSeverOncePerTarget(bool wide, float chance, int remaining)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var (user, weapon) = CreateWeapon(entities, map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(Vector2.UnitX));
            var severing = entities.GetComponent<SeveringProjectileComponent>(weapon);
            severing.Chance = chance;
            var parts = entities.System<SharedBodySystem>().GetBodyChildren(target)
                .Where(part => severing.Parts.Contains(part.Component.PartType)).ToArray();
            Assert.That(parts, Has.Length.EqualTo(5));
            var melee = entities.System<SharedMeleeWeaponSystem>();
            var meleeComp = entities.GetComponent<MeleeWeaponComponent>(weapon);
            Assert.That(wide
                ? melee.AttemptHeavyAttack(user, weapon, meleeComp, [target, target],
                    map.GridCoords.Offset(Vector2.UnitX))
                : melee.AttemptLightAttack(user, weapon, meleeComp, target), Is.True);
            Assert.That(parts.Count(part => part.Component.Body == target), Is.EqualTo(remaining),
                "A duplicate wide-swing target must not receive a second severing attempt.");
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.GreaterThan(0));
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HeadAmputationDoesNotAttributeVitalDamageToTheBlade()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var (user, weapon) = CreateWeapon(entities, map.GridCoords);
            entities.GetComponent<SeveringProjectileComponent>(weapon).Parts = [BodyPartType.Head];
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(Vector2.UnitX));
            var receiver = entities.AddComponent<SeveringMeleeTestReceiverComponent>(target);
            var head = entities.System<SharedBodySystem>().GetBodyChildrenOfType(target, BodyPartType.Head).Single();
            Assert.That(entities.System<SharedMeleeWeaponSystem>().AttemptLightAttack(user, weapon,
                entities.GetComponent<MeleeWeaponComponent>(weapon), target), Is.True);
            Assert.That(head.Component.Body, Is.Null);
            Assert.That(receiver.DamageEvents, Is.GreaterThanOrEqualTo(2), "Removing a vital head also deals bloodloss damage.");
            Assert.That(receiver.AttributedDamageEvents, Is.EqualTo(1), "Vital damage must not trigger another weapon effect.");
            Assert.That(entities.System<SharedBodySystem>().GetBodyChildrenOfType(target, BodyPartType.Arm).Count(), Is.EqualTo(2));
            Assert.That(entities.System<SharedBodySystem>().GetBodyChildrenOfType(target, BodyPartType.Leg).Count(), Is.EqualTo(2));
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissesAndBlockedDamageCannotSever(bool armor)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var (user, weapon) = CreateWeapon(entities, map.GridCoords);
            var target = entities.SpawnEntity(armor ? "ExodusSeveringTestArmoredHuman" : "MobHuman",
                map.GridCoords.Offset(Vector2.UnitX));
            if (!armor)
                entities.AddComponent<GodmodeComponent>(target);
            var parts = entities.System<SharedBodySystem>().GetBodyChildren(target).ToArray();
            var melee = entities.System<SharedMeleeWeaponSystem>();
            var meleeComp = entities.GetComponent<MeleeWeaponComponent>(weapon);
            melee.GetDamage(weapon, user);
            melee.AttemptLightAttackMiss(user, weapon, meleeComp, map.GridCoords);
            Assert.That(parts.All(part => part.Component.Body == target), Is.True);
            meleeComp.NextAttack = TimeSpan.Zero;
            Assert.That(melee.AttemptLightAttack(user, weapon, meleeComp, target), Is.True);
            Assert.That(parts.All(part => part.Component.Body == target), Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.Zero);
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [TestCase("NoBody")]
    [TestCase("NoEligibleParts")]
    [TestCase("Unseverable")]
    [TestCase("Root")]
    public async Task IneligibleTargetsAndPartsStillTakeOrdinaryDamage(string restriction)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var (user, weapon) = CreateWeapon(entities, map.GridCoords);
            var target = entities.SpawnEntity(restriction == "NoBody" ? "ExodusSeveringTestObject" : "MobHuman",
                map.GridCoords.Offset(Vector2.UnitX));
            var severing = entities.GetComponent<SeveringProjectileComponent>(weapon);
            var parts = entities.System<SharedBodySystem>().GetBodyChildren(target).ToArray();
            switch (restriction)
            {
                case "NoEligibleParts":
                    severing.Parts.Clear();
                    break;
                case "Unseverable":
                    foreach (var (_, part) in parts)
                        part.CanSever = false;
                    break;
                case "Root":
                    severing.Parts = [BodyPartType.Torso];
                    break;
            }

            Assert.That(entities.System<SharedMeleeWeaponSystem>().AttemptLightAttack(user, weapon,
                entities.GetComponent<MeleeWeaponComponent>(weapon), target), Is.True);
            Assert.That(parts.All(part => part.Component.Body == target), Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.GreaterThan(0));
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DamageThatDisallowsSeveringKeepsEveryPart()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var (user, weapon) = CreateWeapon(entities, map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(Vector2.UnitX));
            var parts = entities.System<SharedBodySystem>().GetBodyChildren(target).ToArray();
            var damage = new DamageSpecifier { DamageDict = { ["Slash"] = 1 } };
            entities.System<DamageableSystem>().TryChangeDamage(target, damage, origin: user, tool: weapon, canSever: false);
            Assert.That(parts.All(part => part.Component.Body == target), Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.GreaterThan(0));
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExistingProjectilesStillSeverOneArmOrLeg()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(Vector2.UnitX));
            var projectile = entities.SpawnEntity("ExodusSeveringTestProjectile", map.GridCoords);
            var body = entities.System<SharedBodySystem>();
            var limbs = body.GetBodyChildren(target)
                .Where(part => part.Component.PartType is BodyPartType.Arm or BodyPartType.Leg).ToArray();
            var head = body.GetBodyChildrenOfType(target, BodyPartType.Head).Single();
            entities.System<SharedProjectileSystem>().ProjectileCollide(
                (projectile, entities.GetComponent<ProjectileComponent>(projectile),
                    entities.GetComponent<PhysicsComponent>(projectile)), target);
            Assert.That(limbs.Count(part => part.Component.Body == target), Is.EqualTo(3));
            Assert.That(head.Component.Body, Is.EqualTo(target));
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnmarkedMeleeWeaponsPreserveTheAttackerAsDestructionCause()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var (user, weapon) = CreateWeapon(entities, map.GridCoords);
            entities.RemoveComponent<SeveringProjectileComponent>(weapon);
            var target = entities.SpawnEntity("ExodusSeveringTestObject", map.GridCoords.Offset(Vector2.UnitX));
            var receiver = entities.GetComponent<SeveringMeleeTestReceiverComponent>(target);
            Assert.That(entities.System<SharedMeleeWeaponSystem>().AttemptLightAttack(user, weapon,
                entities.GetComponent<MeleeWeaponComponent>(weapon), target), Is.True);
            Assert.That(receiver.DestructionCause, Is.EqualTo(user), "Ordinary mining must retain the attacker's ore-yield modifiers.");
            Assert.That(receiver.AttributedDamageEvents, Is.Zero);
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CloseContactBorgCanSelectItsBladeModuleAndSeverAHead()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("PlayerBorgAsakimCloseContact", map.GridCoords);
            var chassis = entities.GetComponent<BorgChassisComponent>(user);
            var module = chassis.ModuleContainer.ContainedEntities.Single(uid =>
                entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "BorgModuleAsakimMonomolecularBlade");
            entities.System<BorgSystem>().SelectModule(user, module);
            var blade = entities.GetComponent<ItemBorgModuleComponent>(module).ProvidedItems
                .Single(item => entities.HasComponent<SeveringProjectileComponent>(item.Value));
            var weapon = blade.Value;
            var hands = entities.System<SharedHandsSystem>();
            hands.TrySetActiveHand(user, blade.Key);
            Assert.That(hands.GetActiveItem(user), Is.EqualTo(weapon));
            var severing = entities.GetComponent<SeveringProjectileComponent>(weapon);
            Assert.That(severing.Chance, Is.EqualTo(0.5f));
            Assert.That(severing.Parts, Is.EquivalentTo(new[] { BodyPartType.Head, BodyPartType.Arm, BodyPartType.Leg }));
            severing.Chance = 1;
            severing.Parts = [BodyPartType.Head];
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(Vector2.UnitX));
            var head = entities.System<SharedBodySystem>().GetBodyChildrenOfType(target, BodyPartType.Head).Single();
            entities.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
            var melee = entities.GetComponent<MeleeWeaponComponent>(weapon);
            melee.NextAttack = TimeSpan.Zero;
            Assert.That(melee.CanWideSwing, Is.True);
            Assert.That(entities.System<SharedMeleeWeaponSystem>().AttemptLightAttack(user, weapon, melee, target), Is.True);
            Assert.That(head.Component.Body, Is.Null);
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    private static (EntityUid User, EntityUid Weapon) CreateWeapon(IEntityManager entities, EntityCoordinates coordinates)
    {
        var user = entities.SpawnEntity("MobHuman", coordinates);
        var weapon = entities.SpawnEntity("ExodusSeveringTestBlade", coordinates);
        Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, weapon), Is.True);
        entities.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
        entities.GetComponent<MeleeWeaponComponent>(weapon).NextAttack = TimeSpan.Zero;
        return (user, weapon);
    }
}

[RegisterComponent]
public sealed partial class SeveringMeleeTestReceiverComponent : Component
{
    public int DamageEvents;
    public int AttributedDamageEvents;
    public EntityUid? DestructionCause;
}

public sealed class SeveringMeleeTestReceiverSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SeveringMeleeTestReceiverComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<SeveringMeleeTestReceiverComponent, DestructionEventArgs>(OnDestruction);
    }

    private void OnDamageChanged(Entity<SeveringMeleeTestReceiverComponent> ent, ref DamageChangedEvent args)
    {
        ent.Comp.DamageEvents++;
        if (args.Tool != null)
            ent.Comp.AttributedDamageEvents++;
    }

    private void OnDestruction(Entity<SeveringMeleeTestReceiverComponent> ent, ref DestructionEventArgs args)
    {
        ent.Comp.DestructionCause = args.Cause;
    }
}
