using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.NPC.Pet;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Map;

namespace Content.Server._Exodus.NPC.Pet;

public sealed partial class PetSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public const string MasterKey = "PetMaster";

    public const string MasterCoordinatesKey = "PetMasterCoordinates";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PetComponent, ComponentShutdown>(OnPetShutdown);
        SubscribeLocalEvent<PetOwnerMindComponent, MindGotAddedEvent>(OnMindBodyChanged);

        // Run before storage so we can veto a non-master opening the pet's cargo.
        SubscribeLocalEvent<PetComponent, ActivateInWorldEvent>(OnPetActivate,
            before: new[] { typeof(SharedStorageSystem) });
    }

    public void BindOwner(Entity<PetComponent?> pet, EntityUid body)
    {
        if (!Resolve(pet, ref pet.Comp))
            return;

        if (_mind.TryGetMind(body, out var mindId, out _))
        {
            pet.Comp.MasterMind = mindId;
            EnsureComp<PetOwnerMindComponent>(mindId).Pets.Add(pet);
        }

        BindToBody((pet, pet.Comp), body);
    }

    // If master cloned
    public void RebindToBody(Entity<PetComponent?> pet, EntityUid body)
    {
        if (!Resolve(pet, ref pet.Comp))
            return;

        BindToBody((pet, pet.Comp), body);
    }

    private void BindToBody(Entity<PetComponent> pet, EntityUid body)
    {
        if (pet.Comp.Master is { } oldBody && oldBody != body && TryComp<PetMasterComponent>(oldBody, out var oldMaster))
        {
            oldMaster.Pets.Remove(pet);
            if (oldMaster.Pets.Count == 0 && !TerminatingOrDeleted(oldBody))
                RemComp<PetMasterComponent>(oldBody);
        }

        pet.Comp.Master = body;
        MirrorFaction(pet, body);
        EnsureComp<PetMasterComponent>(body).Pets.Add(pet);

        _npc.SetBlackboard(pet, MasterKey, body);
        _npc.SetBlackboard(pet, MasterCoordinatesKey, new EntityCoordinates(body, Vector2.Zero));

        var ev = new PetOwnerChangedEvent(body);
        RaiseLocalEvent(pet, ref ev);
    }

    private void OnMindBodyChanged(Entity<PetOwnerMindComponent> ent, ref MindGotAddedEvent args)
    {
        var newBody = args.Container.Owner;
        foreach (var pet in ent.Comp.Pets)
        {
            if (TryComp<PetComponent>(pet, out var petComp))
                BindToBody((pet, petComp), newBody);
        }
    }

    private void OnPetShutdown(Entity<PetComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Master is { } body && TryComp<PetMasterComponent>(body, out var bodyComp))
        {
            bodyComp.Pets.Remove(ent);
            if (bodyComp.Pets.Count == 0 && !TerminatingOrDeleted(body))
                RemComp<PetMasterComponent>(body);
        }

        if (ent.Comp.MasterMind is { } mind && TryComp<PetOwnerMindComponent>(mind, out var mindComp))
            mindComp.Pets.Remove(ent);
    }

    // Only master may open a pet's cargo storage.
    private void OnPetActivate(Entity<PetComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !HasComp<StorageComponent>(ent))
            return;

        if (IsMaster(ent.Comp, args.User))
            return;

        _popup.PopupEntity(Loc.GetString("pet-access-denied"), ent, args.User);
        args.Handled = true; // consume so storage never opens for non-masters
    }

    public bool IsMaster(PetComponent comp, EntityUid user)
    {
        return comp.MasterMind != null
               && _mind.TryGetMind(user, out var mind, out _)
               && mind == comp.MasterMind;
    }

    // If pokeball destroyed while the pet is deployed make it feral.
    public void MakeFeral(Entity<PetComponent?> pet)
    {
        if (!Resolve(pet, ref pet.Comp))
            return;

        // Capture feral config before PetComponent is stripped below.
        var feralFaction = pet.Comp.FeralFaction;
        var feralCompound = pet.Comp.FeralCompound;

        RemComp<PetFollowerComponent>(pet);
        RemComp<PetWarriorComponent>(pet);
        RemComp<PetComponent>(pet);

        if (TryComp<NpcFactionMemberComponent>(pet, out var faction))
        {
            _faction.ClearFactions((pet.Owner, faction));
            _faction.AddFaction((pet.Owner, faction), feralFaction);
        }

        // Swap to kill them all tree.
        if (TryComp<HTNComponent>(pet, out var htn))
        {
            if (htn.Plan != null)
                _htn.ShutdownPlan(htn);

            htn.RootTask = new HTNCompoundTask { Task = feralCompound };
            _htn.Replan(htn);
        }
    }

    // Make the pet's faction membership same as master.
    private void MirrorFaction(EntityUid pet, EntityUid master)
    {
        if (!TryComp<NpcFactionMemberComponent>(master, out var masterFaction))
            return;

        var petFaction = EnsureComp<NpcFactionMemberComponent>(pet);

        _faction.ClearFactions((pet, petFaction));

        foreach (var f in masterFaction.Factions)
            _faction.AddFaction((pet, petFaction), f.Id);
    }
}
