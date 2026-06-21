using System.Numerics;
using Content.Server.Materials;
using Content.Shared._Exodus.NPC.Pet;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Materials;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Content.Shared.Storage;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.Pet;

public sealed class PetCapsuleSystem : EntitySystem
{
    [Dependency] private PetSystem _pet = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MaterialStorageSystem _material = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    // One shared non-init map holds every stored pet.
    private EntityUid? _storageMap;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PetCapsuleComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<PetCapsuleComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<PetCapsuleComponent, ComponentShutdown>(OnShutdown);
    }

    // If pokeball destroyed.
    private void OnShutdown(Entity<PetCapsuleComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.PetEntity is not { } pet || TerminatingOrDeleted(pet))
            return;

        if (ent.Comp.Stored)
            QueueDel(pet);
        else
            _pet.MakeFeral(pet);
    }

    private void OnExamine(Entity<PetCapsuleComponent> ent, ref ExaminedEvent args)
    {
        var amount = _material.GetMaterialAmount(ent, ent.Comp.Material);
        var cap = TryComp<MaterialStorageComponent>(ent, out var ms) ? ms.StorageLimit ?? 0 : 0;
        var material = Loc.GetString(_proto.Index(ent.Comp.Material).Name);

        args.PushMarkup(Loc.GetString("pet-capsule-examine",
            ("material", material),
            ("amount", amount),
            ("cap", cap),
            ("reconstruct", ent.Comp.ReconstructCost),
            ("revive", ent.Comp.ReviveCost)));
    }

    private void OnUseInHand(Entity<PetCapsuleComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var c = ent.Comp;
        var user = args.User;

        // First activation - bound to the player mind (probably body is better but meh).
        if (c.MasterMind == null)
        {
            if (!_mind.TryGetMind(user, out var mindId, out _))
                return;

            c.MasterMind = mindId;
            DeployFresh(ent, user);
            return;
        }

        // Only the bound owner may use it.
        if (!_mind.TryGetMind(user, out var userMind, out _) || userMind != c.MasterMind)
        {
            _popup.PopupEntity(Loc.GetString("pet-access-denied"), ent, user);
            return;
        }

        if (c.PetEntity is { } pet && !TerminatingOrDeleted(pet))
        {
            if (!c.Stored)
            {
                Store(ent, pet);
                return;
            }

            if (_mobState.IsDead(pet))
            {
                if (!TrySpend(ent, c.ReviveCost))
                {
                    _popup.PopupEntity(Loc.GetString("pet-capsule-insufficient"), ent, user);
                    return;
                }

                RaiseLocalEvent(pet, new RejuvenateEvent());
            }

            Deploy(ent, user, pet);
            return;
        }

        // If pet gibbed reconstruct for material price.
        if (!TrySpend(ent, c.ReconstructCost))
        {
            _popup.PopupEntity(Loc.GetString("pet-capsule-insufficient"), ent, user);
            return;
        }

        DeployFresh(ent, user);
    }

    private void DeployFresh(Entity<PetCapsuleComponent> ent, EntityUid user)
    {
        var pet = Spawn(ent.Comp.Pet, Transform(user).Coordinates);
        ent.Comp.PetEntity = pet;
        ent.Comp.Stored = false;
        _pet.BindOwner(pet, user);
        _audio.PlayPvs(ent.Comp.Sound, ent);
    }

    // Summon stored pet next to the owner.
    private void Deploy(Entity<PetCapsuleComponent> ent, EntityUid user, EntityUid pet)
    {
        _xform.SetCoordinates(pet, Transform(user).Coordinates);
        ent.Comp.Stored = false;
        _pet.RebindToBody(pet, user);
        _audio.PlayPvs(ent.Comp.Sound, ent);
    }

    // Hide pet on storage map.
    private void Store(Entity<PetCapsuleComponent> ent, EntityUid pet)
    {
        // Cargo pets dump inventory onto the floor first.
        if (_container.TryGetContainer(pet, StorageComponent.ContainerId, out var bag))
            _container.EmptyContainer(bag, destination: Transform(pet).Coordinates);

        _xform.SetCoordinates(pet, new EntityCoordinates(GetStorageMap(), Vector2.Zero));
        ent.Comp.Stored = true;
        _audio.PlayPvs(ent.Comp.Sound, ent);
    }

    private bool TrySpend(Entity<PetCapsuleComponent> ent, int cost)
    {
        if (_material.GetMaterialAmount(ent, ent.Comp.Material) < cost)
            return false;

        return _material.TryChangeMaterialAmount(ent, ent.Comp.Material, -cost);
    }

    private EntityUid GetStorageMap()
    {
        if (_storageMap is { } map && !TerminatingOrDeleted(map))
            return map;

        var mapId = _mapManager.CreateMap();
        _mapManager.SetMapPaused(mapId, true);
        _storageMap = _mapManager.GetMapEntityId(mapId);
        return _storageMap.Value;
    }
}
