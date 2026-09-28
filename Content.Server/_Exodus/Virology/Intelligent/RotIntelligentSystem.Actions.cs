using Content.Shared._Exodus.Examine;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Popups;
using Content.Shared.IdentityManagement;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotIntelligentSystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    private void InitializeActions()
    {
        SubscribeLocalEvent<RotIntelligentComponent, RotOpenMenuEvent>(OnMenu);
        SubscribeLocalEvent<RotIntelligentComponent, RotReturnToCoreEvent>(OnReturn);
        SubscribeLocalEvent<RotIntelligentComponent, RotBuildEvent>(OnBuild);
        SubscribeLocalEvent<RotIntelligentComponent, RotRepairEvent>(OnRepair);
        SubscribeLocalEvent<RotIntelligentComponent, RotDissolveEvent>(OnDissolve);
        SubscribeLocalEvent<RotIntelligentComponent, RotAdoptEvent>(OnAdopt);
        SubscribeLocalEvent<RotIntelligentComponent, RotRallyEvent>(OnRally);
        SubscribeLocalEvent<RotIntelligentComponent, RotSelectBuildingMessage>(OnSelect);
        SubscribeLocalEvent<RotIntelligentComponent, RotJumpMessage>(OnJump);
        SubscribeLocalEvent<RotIntelligentComponent, RotCancelProjectMessage>(OnCancel);
        SubscribeLocalEvent<RotIntelligentComponent, RotColonyCommandMessage>(OnCommand);
        SubscribeLocalEvent<RotIntelligentComponent, RemoteExamineEvent>(OnRemoteExamine);
    }

    private void OnMenu(Entity<RotIntelligentComponent> ent, ref RotOpenMenuEvent args)
    {
        if (args.Handled || !IsActiveCore(ent))
            return;
        args.Handled = _ui.TryToggleUi(ent.Owner, RotIntelligentUiKey.Key, ent.Owner);
        if (TryComp<RotColonyStateComponent>(ent, out var state))
            UpdateUi((ent, ent.Comp, state));
    }

    private void OnReturn(Entity<RotIntelligentComponent> ent, ref RotReturnToCoreEvent args)
    {
        if (!args.Handled && IsActiveCore(ent))
            args.Handled = JumpTo(ent, ent);
    }

    private void OnSelect(Entity<RotIntelligentComponent> ent, ref RotSelectBuildingMessage args)
    {
        if (args.Actor != ent.Owner || !IsActiveCore(ent) || !ent.Comp.Buildings.Contains(args.Building)
            || args.Rotation is < 0 or > 3)
            return;
        ent.Comp.SelectedBuilding = args.Building;
        ent.Comp.Rotation = args.Rotation;
        Dirty(ent);
    }

    private void OnBuild(Entity<RotIntelligentComponent> ent, ref RotBuildEvent args)
    {
        if (!args.Handled)
            args.Handled = TryQueueBuilding(ent, args.Target, ent.Comp.SelectedBuilding, ent.Comp.Rotation);
    }

    private void OnRepair(Entity<RotIntelligentComponent> ent, ref RotRepairEvent args)
    {
        if (!args.Handled)
            args.Handled = TryQueueMaintenance(ent, args.Target, RotProjectKind.Repair);
    }

    private void OnDissolve(Entity<RotIntelligentComponent> ent, ref RotDissolveEvent args)
    {
        if (!args.Handled)
            args.Handled = TryQueueMaintenance(ent, args.Target, RotProjectKind.Dissolve);
    }

    private void OnAdopt(Entity<RotIntelligentComponent> ent, ref RotAdoptEvent args)
    {
        if (args.Handled || !IsActiveCore(ent) || TerminatingOrDeleted(args.Target)
            || !HasComp<RotCreatureComponent>(args.Target) || HasComp<RotIntelligentComponent>(args.Target)
            || !CanSee(ent, Transform(args.Target).Coordinates)
            || Transform(args.Target).GridUid != Transform(ent).GridUid)
            return;
        args.Handled = Join(args.Target, ent);
        Feedback(ent, args.Handled ? "rot-intelligent-adopted" : "rot-intelligent-other-colony");
    }

    private void OnRally(Entity<RotIntelligentComponent> ent, ref RotRallyEvent args)
    {
        if (args.Handled || !IsActiveCore(ent) || !args.Target.IsValid(EntityManager)
            || !CanSee(ent, args.Target) || !TryComp<RotColonyStateComponent>(ent, out var state)
            || state.Grid is not { } grid || _transform.GetGrid(args.Target) != grid)
            return;
        state.Rally = args.Target;
        state.RallyUntil = _timing.CurTime + ent.Comp.RallyDuration;
        if (state.RallyMarker is { } old)
            QueueDel(old);
        var marker = Spawn(ent.Comp.RallyMarker, args.Target);
        state.RallyMarker = marker;
        Join(marker, ent);
        args.Handled = true;
        Feedback(ent, "rot-intelligent-rally-set");
    }

    public bool TryGetRally(EntityUid member, out EntityCoordinates point)
    {
        point = default;
        if (!_members.TryComp(member, out var comp) || comp.Core is not { } core || !IsActiveCore(core)
            || HasComp<ActorComponent>(member) || !TryComp<RotColonyStateComponent>(core, out var state)
            || !TryComp<RotIntelligentComponent>(core, out var controller)
            || state.Rally is not { } rally || !rally.IsValid(EntityManager) || _timing.CurTime >= state.RallyUntil
            || !Transform(member).Coordinates.TryDistance(EntityManager, rally, out var distance) || distance > controller.RallyRange)
            return false;
        point = rally;
        return true;
    }

    private void OnJump(Entity<RotIntelligentComponent> ent, ref RotJumpMessage args)
    {
        if (args.Actor == ent.Owner && GetEntity(args.Source) is var target)
            JumpTo(ent, target);
    }

    private bool JumpTo(Entity<RotIntelligentComponent> ent, EntityUid target)
    {
        if (!IsActiveCore(ent) || TerminatingOrDeleted(target) || ent.Comp.Eye is not { } eye
            || TerminatingOrDeleted(eye) || !_members.TryComp(target, out var member) || member.Core != ent.Owner
            || !CanJumpTo((target, member), ent.Owner) || _containers.IsEntityInContainer(target)
            || _transform.GetMapCoordinates(target).MapId != _transform.GetMapCoordinates(ent).MapId)
            return false;
        StopPiloting(ent);
        _transform.SetCoordinates(eye, Transform(target).Coordinates);
        AttachEye(ent);
        if (TryComp<RotColonyStateComponent>(ent, out var state))
        {
            state.ViewFrame = null;
            state.NextVision = TimeSpan.Zero;
        }
        return true;
    }

    private bool CanJumpTo(Entity<RotColonyMemberComponent> member, EntityUid core)
    {
        if (Transform(member).MapUid != Transform(core).MapUid)
            return false;
        if (_mobQuery.HasComp(member) && !_mobs.IsAlive(member))
            return false;
        if (_visionQuery.TryComp(member, out var vision) && vision.Enabled)
            return true;
        return _colonyQuery.TryComp(core, out var state) && state.Alerts.ContainsKey(member)
            && CanSee(core, Transform(member).Coordinates);
    }

    private void OnCancel(Entity<RotIntelligentComponent> ent, ref RotCancelProjectMessage args)
    {
        if (args.Actor == ent.Owner && TryComp<RotConstructionComponent>(GetEntity(args.Project), out var job)
            && job.Core == ent.Owner)
            CancelProject(GetEntity(args.Project), refund: true);
    }

    private void OnCommand(Entity<RotIntelligentComponent> ent, ref RotColonyCommandMessage args)
    {
        if (args.Actor != ent.Owner || !IsActiveCore(ent))
            return;
        switch (args.Command)
        {
            case RotColonyCommand.Return:
                JumpTo(ent, ent);
                break;
            case RotColonyCommand.Pilot:
                TryPilot(ent);
                break;
            case RotColonyCommand.Unanchor:
                TryUnanchor(ent);
                break;
            case RotColonyCommand.Rotate:
                ent.Comp.Rotation = (ent.Comp.Rotation + 1) & 3;
                Dirty(ent);
                break;
        }
    }

    private void Feedback(EntityUid core, string key)
    {
        var message = Loc.GetString(key);
        if (TryComp<RotColonyStateComponent>(core, out var state))
        {
            state.Feedback = message;
            state.NextUi = TimeSpan.Zero;
        }
        _popup.PopupCursor(message, core);
    }

    private void UpdateUi(Entity<RotIntelligentComponent, RotColonyStateComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, RotIntelligentUiKey.Key))
            return;
        var cameras = new List<RotCameraEntry>();
        foreach (var uid in ent.Comp2.Members)
        {
            if (TerminatingOrDeleted(uid) || !_members.TryComp(uid, out var member)
                || !CanJumpTo((uid, member), ent.Owner) || _containers.IsEntityInContainer(uid))
                continue;
            cameras.Add(new(GetNetEntity(uid), Identity.Name(uid, EntityManager), ent.Comp2.Alerts.ContainsKey(uid)));
        }
        var jobs = new List<RotProjectEntry>();
        foreach (var uid in ent.Comp2.Projects)
        {
            if (!TryComp<RotConstructionComponent>(uid, out var project) || project.Settled)
                continue;
            var progress = project.WaitingForNetwork ? 0f : (float)((_timing.CurTime - project.Started) / project.Duration);
            var name = project.Building is { } building ? Loc.GetString(_prototypes.Index(building).Name)
                : Loc.GetString(project.Kind == RotProjectKind.Repair ? "rot-intelligent-repair" : "rot-intelligent-dissolve");
            jobs.Add(new(GetNetEntity(uid), name, Math.Clamp(progress, 0f, 1f)));
        }
        _ui.SetUiState(ent.Owner, RotIntelligentUiKey.Key, new RotIntelligentUiState(ent.Comp1.Biomass, ent.Comp1.Capacity,
            ent.Comp1.Income, ent.Comp1.MaxProjects, cameras, jobs, ent.Comp2.Feedback));
    }
}
