using Content.Server._NF.Construction.Components;
using Content.Server._Exodus.Visuals; // Exodus - generic entity link visuals
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Stack;
using Content.Server.Storage.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Construction.Components;
using Content.Shared.Exchanger;
using Content.Shared.Examine; // Exodus - bluespace RPED
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.Utility;
using Content.Shared.Wires;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Construction.Prototypes;

namespace Content.Server._NF.Construction;

public sealed partial class PartExchangerSystem : EntitySystem
{
    [Dependency] private ConstructionSystem _construction = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StorageSystem _storage = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private EntityManager _entity = default!;
    [Dependency] private EntityLinkVisualSystem _linkVisual = default!; // Exodus - generic entity link visuals
    [Dependency] private SharedInteractionSystem _interaction = default!; // Exodus - bluespace RPED
    [Dependency] private ExamineSystemShared _examine = default!; // Exodus - bluespace RPED

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<PartExchangerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<PartExchangerComponent, ExchangerDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<PartExchangerComponent, DoAfterAttemptEvent<ExchangerDoAfterEvent>>(OnDoAfterAttempt); // Exodus - bluespace RPED
    }

    private struct UpgradePartState
    {
        public MachinePartComponent Part;
        public StackComponent? Stack;
        public bool InContainer;
    }

    // Exodus-begin - bluespace RPED
    private void OnDoAfterAttempt(Entity<PartExchangerComponent> ent, ref DoAfterAttemptEvent<ExchangerDoAfterEvent> args)
    {
        var target = GetEntity(args.Event.ExchangeTarget);
        if (!CanExchange(ent, args.DoAfter.Args.User, target))
            args.Cancel();
    }

    private void OnDoAfter(Entity<PartExchangerComponent> ent, ref ExchangerDoAfterEvent args)
    {
        if (args.Cancelled)
        {
            ent.Comp.AudioStream = _audio.Stop(ent.Comp.AudioStream);
            return;
        }

        if (args.Handled)
            return;

        var target = GetEntity(args.ExchangeTarget);
        if (!CanExchange(ent, args.User, target))
        {
            ent.Comp.AudioStream = _audio.Stop(ent.Comp.AudioStream);
            return;
        }

        if (TryExchangeParts(ent, target))
            ShowExchangeVisual(ent, args.User, target);

        args.Handled = true;
    }

    private bool TryExchangeParts(Entity<PartExchangerComponent> ent, EntityUid target)
    {
        if (!TryComp<StorageComponent>(ent, out var storage) || storage.Container == null)
            return false;

        var partsByType = new Dictionary<ProtoId<MachinePartPrototype>, List<(EntityUid, UpgradePartState)>>();

        // Insert the contained parts into a dictionary for indexing.
        // Note: these parts remain in the starting container.
        foreach (var item in storage.Container.ContainedEntities)
        {
            if (_construction.GetMachinePartState(item, out var partState))
            {
                UpgradePartState upgrade;
                upgrade.Part = partState.Part;
                upgrade.Stack = partState.Stack;
                upgrade.InContainer = true;

                var partType = upgrade.Part.PartType;
                if (!partsByType.ContainsKey(partType))
                    partsByType[partType] = new List<(EntityUid, UpgradePartState)>();
                partsByType[partType].Add((item, upgrade));
            }
        }

        // Exchange machine parts with the machine or frame.
        var exchanged = false;
        if (TryComp<MachineComponent>(target, out var machine))
            exchanged = TryExchangeMachineParts(machine, target, ent, partsByType);
        else if (TryComp<MachineFrameComponent>(target, out var machineFrame))
            exchanged = TryConstructMachineParts(machineFrame, target, ent, partsByType);

        return exchanged;
    }

    private void ShowExchangeVisual(Entity<PartExchangerComponent> ent, EntityUid user, EntityUid target)
    {
        if (ent.Comp.ExchangeVisualStyle is { } style)
            _linkVisual.TryShowTemporaryLink(user, target, style, ent.Comp.ExchangeVisualDuration);
    }
    // Exodus-end

    private bool TryExchangeMachineParts(MachineComponent machine, EntityUid uid, EntityUid storageUid, Dictionary<ProtoId<MachinePartPrototype>, List<(EntityUid part, UpgradePartState state)>> partsByType) // Exodus - report successful exchange
    {
        var board = machine.BoardContainer.ContainedEntities.FirstOrNull();

        if (board == null || !TryComp<MachineBoardComponent>(board, out var macBoardComp))
            return false;

        // Add all components in the machine to form a complete set of available components.
        foreach (var item in new ValueList<EntityUid>(machine.PartContainer.ContainedEntities)) //clone so don't modify during enumeration
        {
            if (_construction.GetMachinePartState(item, out var partState))
            {
                UpgradePartState upgrade;
                upgrade.Part = partState.Part;
                upgrade.Stack = partState.Stack;
                upgrade.InContainer = false;

                var partType = upgrade.Part.PartType;
                if (!partsByType.ContainsKey(partType))
                    partsByType[partType] = new List<(EntityUid, UpgradePartState)>();
                partsByType[partType].Add((item, upgrade));

                _container.RemoveEntity(uid, item);
            }
        }

        // Sort by rating in descending order (highest rated parts first)
        foreach (var (partKey, partList) in partsByType)
            partList.Sort((x, y) => y.state.Part.Rating.CompareTo(x.state.Part.Rating));

        var updatedParts = new List<(EntityUid id, MachinePartState state, int index)>();
        foreach (var (type, amount) in macBoardComp.Requirements)
        {
            if (partsByType.ContainsKey(type))
            {
                var partsNeeded = amount;
                int index = 0;
                foreach ((var part, var state) in partsByType[type])
                {
                    // No more space for components
                    if (partsNeeded <= 0)
                        break;

                    if (state.Stack is not null)
                    {
                        var count = state.Stack.Count;
                        // Entire stack is needed, add it to the things to bring over.
                        if (count <= partsNeeded)
                        {
                            MachinePartState partState;
                            partState.Part = state.Part;
                            partState.Stack = state.Stack;

                            updatedParts.Add((part, partState, index));
                            partsNeeded -= count;
                        }
                        else
                        {
                            // Partial stack is needed, split off what we need, ensure the new entry is moved.
                            EntityUid splitStack = _stack.Split(part, partsNeeded, Transform(uid).Coordinates, state.Stack) ?? EntityUid.Invalid;

                            if (splitStack == EntityUid.Invalid)
                                continue;

                            // Create a new MachinePartState out of our new entity
                            if (_construction.GetMachinePartState(splitStack, out var splitState))
                            {
                                updatedParts.Add((splitStack, splitState, -1)); // Use -1 for index, nothing to remove
                                partsNeeded = 0;
                            }
                        }
                    }
                    else
                    {
                        // Not a stack, move the single part.
                        MachinePartState partState;
                        partState.Part = state.Part;
                        partState.Stack = state.Stack;

                        updatedParts.Add((part, partState, index));
                        partsNeeded--;
                    }
                    // Adjust the index for parts being removed from the container.
                    index++;
                }
            }
        }

        // Move selected parts to the machine, removing them from the dictionary of contained parts.
        // Iterate through list backwards, remove later entries first (maintain validity of earlier indices).
        for (int i = updatedParts.Count - 1; i >= 0; i--)
        {
            var part = updatedParts[i];
            bool inserted = _container.Insert(part.id, machine.PartContainer);
            if (part.index >= 0)
                partsByType[part.state.Part.PartType].RemoveAt(part.index);
        }

        //Put the unused parts back into the container (if they aren't already there)
        foreach (var (partType, partSet) in partsByType)
        {
            foreach (var partState in partSet)
            {
                if (!partState.state.InContainer)
                    _storage.Insert(storageUid, partState.part, out _, playSound: false);
            }
        }
        _construction.RefreshParts(uid, machine);
        return true; // Exodus - report successful exchange
    }

    private bool TryConstructMachineParts(MachineFrameComponent machine, EntityUid uid, EntityUid storageEnt, Dictionary<ProtoId<MachinePartPrototype>, List<(EntityUid part, UpgradePartState state)>> partsByType) // Exodus - report successful exchange
    {
        var board = machine.BoardContainer.ContainedEntities.FirstOrNull();

        if (!machine.HasBoard || !TryComp<MachineBoardComponent>(board, out var macBoardComp))
            return false;

        // Add all components in the machine to form a complete set of available components.
        foreach (var item in new ValueList<EntityUid>(machine.PartContainer.ContainedEntities)) //clone so don't modify during enumeration
        {
            if (_construction.GetMachinePartState(item, out var partState))
            {
                // Construct our entry
                UpgradePartState upgrade;
                upgrade.Part = partState.Part;
                upgrade.Stack = partState.Stack;
                upgrade.InContainer = false;

                // Add it to the table
                var partType = upgrade.Part.PartType;
                if (!partsByType.ContainsKey(partType))
                    partsByType[partType] = new List<(EntityUid, UpgradePartState)>();
                partsByType[partType].Add((item, upgrade));

                // Make sure the construction status is consistent with the removed parts.
                machine.Progress[partType] -= partState.Quantity();
                machine.Progress[partType] = int.Max(0, machine.Progress[partType]); // Ensure progress isn't negative.

                _container.RemoveEntity(uid, item);
            }
        }

        // Sort parts in descending order of rating (highest rated parts first)
        foreach (var partList in partsByType.Values)
            partList.Sort((x, y) => y.state.Part.Rating.CompareTo(x.state.Part.Rating));

        var updatedParts = new List<(EntityUid id, MachinePartState state, int index)>();
        foreach (var (type, amount) in macBoardComp.Requirements)
        {
            if (partsByType.ContainsKey(type))
            {
                var partsNeeded = amount;
                var index = 0;
                foreach ((var part, var state) in partsByType[type])
                {
                    // No more space for components
                    if (partsNeeded <= 0)
                        break;

                    if (state.Stack is not null)
                    {
                        var count = state.Stack.Count;
                        // Entire stack is needed, add it to the things to bring over.
                        if (count <= partsNeeded)
                        {
                            MachinePartState partState;
                            partState.Part = state.Part;
                            partState.Stack = state.Stack;

                            updatedParts.Add((part, partState, index));
                            partsNeeded -= count;
                        }
                        else
                        {
                            // Partial stack is needed, split off what we need, ensure the new entry is moved.
                            EntityUid splitStack = _stack.Split(part, partsNeeded, Transform(uid).Coordinates, state.Stack) ?? EntityUid.Invalid;

                            if (splitStack == EntityUid.Invalid)
                                continue;

                            // Create a new MachinePartState out of our new entity
                            if (_construction.GetMachinePartState(splitStack, out var splitState))
                            {
                                updatedParts.Add((splitStack, splitState, -1)); // New entity, nothing to remove, set index to -1 to flag this.
                                partsNeeded = 0;
                            }
                        }
                    }
                    else
                    {
                        // Not a stack, move the single part.
                        MachinePartState partState;
                        partState.Part = state.Part;
                        partState.Stack = state.Stack;

                        updatedParts.Add((part, partState, index));
                        partsNeeded--;
                    }
                    // Adjust the index for parts being removed from the container.
                    index++;
                }
            }
        }

        // Move selected parts to the machine, removing them from the dictionary of contained parts.
        // Iterate through list backwards, remove later entries first (maintain validity of earlier indices).
        for (int i = updatedParts.Count - 1; i >= 0; i--)
        {
            var part = updatedParts[i];
            _container.Insert(part.id, machine.PartContainer, force: true);
            if (part.index >= 0)
                partsByType[part.state.Part.PartType].RemoveAt(part.index);
            machine.Progress[part.state.Part.PartType] += part.state.Quantity();
        }

        //Put the unused parts back into the container (if they aren't already there)
        foreach (var (partType, partSet) in partsByType)
        {
            foreach (var partState in partSet)
            {
                if (!partState.state.InContainer)
                    _storage.Insert(storageEnt, partState.part, out _, playSound: false); // Exodus - return unused frame parts to the RPED
            }
        }
        return true; // Exodus - report successful exchange
    }

    // Exodus-begin - configurable exchanger range
    private void OnAfterInteract(Entity<PartExchangerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Target is not { } target)
            return;

        if (!HasComp<MachineComponent>(target) && !HasComp<MachineFrameComponent>(target))
            return;

        var (uid, component) = ent;

        if (!CanReachExchangeTarget(ent, args.User, target))
            return;

        if (!component.IgnorePanel &&
            TryComp<WiresPanelComponent>(target, out var panel) &&
            !panel.Open)
        {
            _popup.PopupEntity(Loc.GetString("construction-step-condition-wire-panel-open"),
                target);
            return;
        }

        if (component.InstantExchange)
        {
            ShowExchangeVisual(ent, args.User, target);
            TryExchangeParts(ent, target);
            _audio.PlayPvs(component.ExchangeSound, uid);
            args.Handled = true;
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager,
            args.User,
            component.ExchangeDuration,
            new ExchangerDoAfterEvent(GetNetEntity(target)),
            uid,
            used: uid)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            AttemptFrequency = AttemptFrequency.EveryTick
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
            return;

        args.Handled = true;

        var audioStream = _audio.PlayPvs(component.ExchangeSound, uid);
        if (audioStream != null)
            component.AudioStream = audioStream.Value.Entity;
    }

    private bool CanExchange(Entity<PartExchangerComponent> ent, EntityUid user, EntityUid target)
    {
        return CanReachExchangeTarget(ent, user, target) &&
               (ent.Comp.IgnorePanel ||
                !TryComp<WiresPanelComponent>(target, out var panel) ||
                panel.Open);
    }

    private bool CanReachExchangeTarget(Entity<PartExchangerComponent> ent, EntityUid user, EntityUid target)
    {
        if (Deleted(user) || Deleted(target))
            return false;

        if (!HasComp<MachineComponent>(target) && !HasComp<MachineFrameComponent>(target))
            return false;

        if (!ent.Comp.DoDistanceCheck)
            return true;

        if (ent.Comp.ExchangeRange <= 0f)
            return false;

        return ent.Comp.UseLineOfSight
            ? _examine.InRangeUnOccluded(user, target, ent.Comp.ExchangeRange)
            : _interaction.InRangeUnobstructed(user, target, ent.Comp.ExchangeRange);
    }
    // Exodus-end
}
