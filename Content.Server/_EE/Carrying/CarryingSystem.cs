using System.Collections.Generic; // Exodus multi-carry
using System.Numerics;
using System.Threading;
using Content.Server.DoAfter;
using Content.Server.Resist;
using Content.Server.Popups;
using Content.Server.Inventory;
using Content.Server.Nyanotrasen.Item.PseudoItem;
using Content.Shared.Mobs;
using Content.Shared.DoAfter;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands;
using Content.Shared.Stunnable;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Content.Shared.Climbing.Events;
using Content.Shared.Carrying;
using Content.Shared.Contests;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Content.Shared.ActionBlocker;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Throwing;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nyanotrasen.Item.PseudoItem;
using Content.Shared.Resist; // Exodus
using Content.Shared.Storage;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Server.GameObjects;

namespace Content.Server.Carrying
{
    public sealed partial class CarryingSystem : EntitySystem
    {
        [Dependency] private VirtualItemSystem _virtualItemSystem = default!;
        [Dependency] private CarryingSlowdownSystem _slowdown = default!;
        [Dependency] private DoAfterSystem _doAfterSystem = default!;
        [Dependency] private StandingStateSystem _standingState = default!;
        [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
        [Dependency] private PullingSystem _pullingSystem = default!;
        [Dependency] private MobStateSystem _mobStateSystem = default!;
        [Dependency] private EscapeInventorySystem _escapeInventorySystem = default!;
        [Dependency] private PopupSystem _popupSystem = default!;
        [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
        [Dependency] private PseudoItemSystem _pseudoItem = default!;
        [Dependency] private ContestsSystem _contests = default!;
        [Dependency] private TransformSystem _transform = default!;

        private readonly List<(EntityUid Carrier, EntityUid Carried)> _pendingDrops = new(); // Exodus multi-carry

        public const float BaseDistanceCoeff = 0.5f; // Frontier: default throwing speed reduction
        public const float MaxDistanceCoeff = 1.0f; // Frontier: default throwing speed reduction
        public const float DefaultMaxThrowDistance = 4.0f; // Frontier: maximum throwing distance

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<CarriableComponent, GetVerbsEvent<AlternativeVerb>>(AddCarryVerb);
            SubscribeLocalEvent<CarryingComponent, GetVerbsEvent<InnateVerb>>(AddInsertCarriedVerb);
            SubscribeLocalEvent<CarryingComponent, VirtualItemDeletedEvent>(OnVirtualItemDeleted);
            SubscribeLocalEvent<CarryingComponent, BeforeThrowEvent>(OnThrow);
            SubscribeLocalEvent<CarryingComponent, EntParentChangedMessage>(OnParentChanged); // Exodus multi-carry
            SubscribeLocalEvent<CarryingComponent, MobStateChangedEvent>(OnMobStateChanged); // Exodus multi-carry
            SubscribeLocalEvent<CarryingComponent, Robust.Shared.GameObjects.EntityTerminatingEvent>(OnCarrierTerminating); // Exodus multi-carry cleanup
            SubscribeLocalEvent<BeingCarriedComponent, InteractionAttemptEvent>(OnInteractionAttempt);
            SubscribeLocalEvent<BeingCarriedComponent, MoveInputEvent>(OnMoveInput);
            SubscribeLocalEvent<BeingCarriedComponent, UpdateCanMoveEvent>(OnMoveAttempt);
            SubscribeLocalEvent<BeingCarriedComponent, StandAttemptEvent>(OnStandAttempt);
            SubscribeLocalEvent<BeingCarriedComponent, GettingInteractedWithAttemptEvent>(OnInteractedWith);
            SubscribeLocalEvent<BeingCarriedComponent, PullAttemptEvent>(OnPullAttempt);
            SubscribeLocalEvent<BeingCarriedComponent, StartClimbEvent>(OnStartClimb);
            SubscribeLocalEvent<BeingCarriedComponent, BuckledEvent>(OnBuckleChange);
            SubscribeLocalEvent<BeingCarriedComponent, UnbuckledEvent>(OnBuckleChange);
            SubscribeLocalEvent<BeingCarriedComponent, StrappedEvent>(OnBuckleChange);
            SubscribeLocalEvent<BeingCarriedComponent, UnstrappedEvent>(OnBuckleChange);
            SubscribeLocalEvent<BeingCarriedComponent, Robust.Shared.GameObjects.EntityTerminatingEvent>(OnCarriedTerminating); // Exodus multi-carry cleanup
            SubscribeLocalEvent<CarriableComponent, CarryDoAfterEvent>(OnDoAfter);
        }

        private void AddCarryVerb(EntityUid uid, CarriableComponent component, GetVerbsEvent<AlternativeVerb> args)
        {
            // Exodus multi-carry: no longer block when CarryingComponent exists; CanCarry checks free hands.
            if (!args.CanInteract || !args.CanAccess || !_mobStateSystem.IsAlive(args.User)
                || !CanCarry(args.User, uid, component)
                || HasComp<BeingCarriedComponent>(args.User) || HasComp<BeingCarriedComponent>(args.Target)
                || args.User == args.Target)
                return;

            AlternativeVerb verb = new()
            {
                Act = () =>
                {
                    StartCarryDoAfter(args.User, uid, component);
                },
                Text = Loc.GetString("carry-verb"),
                Priority = 2
            };
            args.Verbs.Add(verb);
        }

        private void AddInsertCarriedVerb(EntityUid uid, CarryingComponent component, GetVerbsEvent<InnateVerb> args)
        {
            // If the person is carrying someone, and the carried person is a pseudo-item, and the target entity is a storage,
            // then add an action to insert the carried entity into the target
            var toInsert = args.Using;
            // Exodus multi-carry: Using can be any free-hand item; only act on entities actually being carried.
            if (toInsert is not { Valid: true } || !args.CanAccess
                || !component.Carried.Contains(toInsert.Value)
                || !TryComp<PseudoItemComponent>(toInsert, out var pseudoItem)
                || !TryComp<StorageComponent>(args.Target, out var storageComp)
                || !_pseudoItem.CheckItemFits((toInsert.Value, pseudoItem), (args.Target, storageComp)))
                return;

            InnateVerb verb = new()
            {
                Act = () =>
                {
                    DropCarried(uid, toInsert.Value);
                    _pseudoItem.TryInsert(args.Target, toInsert.Value, pseudoItem, storageComp);
                },
                Text = Loc.GetString("action-name-insert-other", ("target", toInsert)),
                Priority = 2
            };
            args.Verbs.Add(verb);
        }

        /// <summary>
        /// Since the carried entity is stored as virtual items per <see cref="CarriableComponent.FreeHandsRequired"/>, when deleted we want to drop them.
        /// </summary>
        private void OnVirtualItemDeleted(Entity<CarryingComponent> ent, ref VirtualItemDeletedEvent args)
        {
            if (!HasComp<CarriableComponent>(args.BlockingEntity)
                || !ent.Comp.Carried.Contains(args.BlockingEntity))
                return;

            DropCarried(ent, args.BlockingEntity);
        }

        /// <summary>
        /// Basically using virtual item passthrough to throw the carried person. A new age!
        /// Maybe other things besides throwing should use virt items like this...
        /// </summary>
        private void OnThrow(EntityUid uid, CarryingComponent component, ref BeforeThrowEvent args)
        {
            // Exodus multi-carry: pull also creates virtual items for carriable entities; only remap throws for carried ones.
            if (!TryComp<VirtualItemComponent>(args.ItemUid, out var virtItem)
                || !HasComp<CarriableComponent>(virtItem.BlockingEntity)
                || !component.Carried.Contains(virtItem.BlockingEntity))
                return;

            args.ItemUid = virtItem.BlockingEntity;

            var contestCoeff = _contests.MassContest(uid, virtItem.BlockingEntity, false, 2f) // Frontier: "args.throwSpeed *="<"var contestCoeff ="
                                * _contests.StaminaContest(uid, virtItem.BlockingEntity);

            // Frontier: sanitize our range regardless of CVar values - TODO: variable throw distance ranges (via traits, etc.)
            contestCoeff = float.Min(BaseDistanceCoeff * contestCoeff, MaxDistanceCoeff);
            if (args.Direction.Length() > DefaultMaxThrowDistance * contestCoeff)
                args.Direction = args.Direction.Normalized() * DefaultMaxThrowDistance * contestCoeff;
            // End Frontier
        }

        // Exodus-begin: multi-carry
        private void OnParentChanged(Entity<CarryingComponent> ent, ref EntParentChangedMessage args)
        {
            var xform = Transform(ent);
            if (xform.MapUid != args.OldMapId || xform.ParentUid == xform.GridUid)
                return;

            DropAllCarried(ent);
        }

        private void OnMobStateChanged(Entity<CarryingComponent> ent, ref MobStateChangedEvent args)
        {
            DropAllCarried(ent);
        }

        private void OnCarrierTerminating(Entity<CarryingComponent> ent, ref Robust.Shared.GameObjects.EntityTerminatingEvent args)
        {
            var carriedList = new List<EntityUid>(ent.Comp.Carried);

            foreach (var carried in carriedList)
            {
                CleanupCarriedVictim(carried);
            }

            ent.Comp.Carried.Clear();
        }
        // Exodus-end

        /// <summary>
        /// Only let the person being carried interact with their carrier and things on their person.
        /// </summary>
        private void OnInteractionAttempt(EntityUid uid, BeingCarriedComponent component, InteractionAttemptEvent args)
        {
            if (args.Target == null)
                return;

            var targetParent = Transform(args.Target.Value).ParentUid;

            if (args.Target.Value != component.Carrier && targetParent != component.Carrier && targetParent != uid)
                args.Cancelled = true;
        }

        /// <summary>
        /// Try to escape via the escape inventory system.
        /// </summary>
        private void OnMoveInput(EntityUid uid, BeingCarriedComponent component, ref MoveInputEvent args)
        {
            if (!TryComp<CanEscapeInventoryComponent>(uid, out var escape)
                || !args.HasDirectionalMovement)
                return;

            // Check if the victim is in any way incapacitated, and if not make an escape attempt.
            // Escape time scales with the inverse of a mass contest. Being lighter makes escape harder.
            if (_actionBlockerSystem.CanInteract(uid, component.Carrier))
            {
                var disadvantage = _contests.MassContest(component.Carrier, uid, false, 2f);
                _escapeInventorySystem.AttemptEscape(uid, component.Carrier, escape, disadvantage);
            }
        }

        private void OnMoveAttempt(EntityUid uid, BeingCarriedComponent component, UpdateCanMoveEvent args)
        {
            args.Cancel();
        }

        private void OnStandAttempt(EntityUid uid, BeingCarriedComponent component, StandAttemptEvent args)
        {
            args.Cancel();
        }

        private void OnInteractedWith(EntityUid uid, BeingCarriedComponent component, GettingInteractedWithAttemptEvent args)
        {
            if (args.Uid != component.Carrier)
                args.Cancelled = true;
        }

        private void OnPullAttempt(EntityUid uid, BeingCarriedComponent component, PullAttemptEvent args)
        {
            args.Cancelled = true;
        }

        private void OnStartClimb(EntityUid uid, BeingCarriedComponent component, ref StartClimbEvent args)
        {
            DropCarried(component.Carrier, uid);
        }

        private void OnBuckleChange<TEvent>(EntityUid uid, BeingCarriedComponent component, TEvent args)
        {
            DropCarried(component.Carrier, uid);
        }

        // Exodus-begin: multi-carry cleanup
        private void OnCarriedTerminating(Entity<BeingCarriedComponent> ent, ref Robust.Shared.GameObjects.EntityTerminatingEvent args)
        {
            RemoveCarriedFromCarrier(ent.Comp.Carrier, ent.Owner);
        }
        // Exodus-end

        private void OnDoAfter(Entity<CarriableComponent> ent, ref CarryDoAfterEvent args) // Exodus: modern event signature
        {
            ent.Comp.CancelToken = null;
            if (args.Handled || args.Cancelled
                || !CanCarry(args.Args.User, ent.Owner, ent.Comp))
                return;

            if (Carry(args.Args.User, ent)) // Exodus multi-carry
                args.Handled = true;
        }

        private void StartCarryDoAfter(EntityUid carrier, EntityUid carried, CarriableComponent component)
        {
            if (!TryComp<PhysicsComponent>(carrier, out var carrierPhysics)
                || !TryComp<PhysicsComponent>(carried, out var carriedPhysics)
                || carriedPhysics.Mass > carrierPhysics.Mass * 2f)
            {
                _popupSystem.PopupEntity(Loc.GetString("carry-too-heavy"), carried, carrier, Shared.Popups.PopupType.SmallCaution);
                return;
            }

            var length = component.PickupDuration // Frontier: removed outer TimeSpan.FromSeconds()
                        * _contests.MassContest(carriedPhysics, carrierPhysics, false, 4f)
                        * _contests.StaminaContest(carrier, carried)
                        * (_standingState.IsDown(carried) ? 0.5f : 1);

            // Frontier: sanitize pickup time duration regardless of CVars - no near-instant pickups.
            var duration = TimeSpan.FromSeconds(
                float.Clamp(length,
                component.MinPickupDuration,
                component.MaxPickupDuration));
            // End Frontier

            component.CancelToken = new CancellationTokenSource();

            var ev = new CarryDoAfterEvent();
            var args = new DoAfterArgs(EntityManager, carrier, duration, ev, carried, target: carried) // Frontier: length<duration
            {
                BreakOnMove = true,
                NeedHand = true,
                MultiplyDelay = false, // Goobstation
            };

            if (!_doAfterSystem.TryStartDoAfter(args)) // Exodus multi-carry
            {
                component.CancelToken = null;
                return;
            }

            // Show a popup to the person getting picked up
            _popupSystem.PopupEntity(Loc.GetString("carry-started", ("carrier", carrier)), carried, carried);
        }

        // Exodus-begin: multi-carry
        private bool Carry(EntityUid carrier, Entity<CarriableComponent> carried)
        {
            if (TryComp<PullableComponent>(carried, out var pullable))
                _pullingSystem.TryStopPull(carried, pullable);

            _transform.AttachToGridOrMap(carrier);
            _transform.AttachToGridOrMap(carried);
            _transform.SetCoordinates(carried, Transform(carrier).Coordinates);
            _transform.SetParent(carried, carrier);

            if (!TrySpawnCarryVirtualItems(carried, carrier))
            {
                _transform.AttachToGridOrMap(carried);
                return false;
            }

            var carryingComp = EnsureComp<CarryingComponent>(carrier);
            var carriedComp = EnsureComp<BeingCarriedComponent>(carried);
            EnsureComp<KnockedDownComponent>(carried);

            carryingComp.Carried.Add(carried);
            carriedComp.Carrier = carrier;

            RecalculateCarrySlowdown((carrier, carryingComp));
            _actionBlockerSystem.UpdateCanMove(carried);
            return true;
        }
        // Exodus-end

        public bool TryCarry(EntityUid carrier, EntityUid toCarry, CarriableComponent? carriedComp = null)
        {
            if (!Resolve(toCarry, ref carriedComp, false)
                || !CanCarry(carrier, toCarry, carriedComp)
                || HasComp<BeingCarriedComponent>(carrier)
                || HasComp<ItemComponent>(carrier)
                || TryComp<PhysicsComponent>(carrier, out var carrierPhysics)
                && TryComp<PhysicsComponent>(toCarry, out var toCarryPhysics)
                && carrierPhysics.Mass < toCarryPhysics.Mass * 2f)
                return false;

            return Carry(carrier, (toCarry, carriedComp)); // Exodus multi-carry
        }

        // Exodus-begin: multi-carry
        public void DropCarried(EntityUid carrier, EntityUid carried)
        {
            CleanupCarriedVictim(carried);
            RemoveCarriedFromCarrier(carrier, carried);
        }

        /// <summary>
        /// Clears carry state on the victim when the carrier is missing or deleted.
        /// </summary>
        public void CleanupCarriedVictim(EntityUid carried)
        {
            if (TerminatingOrDeleted(carried))
                return;

            // Exodus: remove immediately so that StandAttempt / UpdateCanMove see the blockers gone.
            // Deferred removal left BeingCarried/KnockedDown active during re-enable, causing permanent no-move after drop.
            RemComp<BeingCarriedComponent>(carried);
            RemComp<KnockedDownComponent>(carried);

            _actionBlockerSystem.UpdateCanMove(carried);
            _transform.AttachToGridOrMap(carried);
            _standingState.Stand(carried);

            if (TryComp<CanEscapeInventoryComponent>(carried, out var escape) && escape.DoAfter != null)
                _doAfterSystem.Cancel(escape.DoAfter);
        }

        private void RemoveCarriedFromCarrier(EntityUid carrier, EntityUid carried)
        {
            if (TerminatingOrDeleted(carrier))
                return;

            if (!TryComp<CarryingComponent>(carrier, out var carrying))
            {
                RemComp<CarryingSlowdownComponent>(carrier);
                _movementSpeed.RefreshMovementSpeedModifiers(carrier);
                _virtualItemSystem.DeleteInHandsMatching(carrier, carried);
                return;
            }

            carrying.Carried.Remove(carried);
            PruneCarried((carrier, carrying));
            FinalizeCarryingState((carrier, carrying));
            _virtualItemSystem.DeleteInHandsMatching(carrier, carried);
        }

        private void DropAllCarried(Entity<CarryingComponent> carrier)
        {
            PruneCarried(carrier);

            if (carrier.Comp.Carried.Count == 0)
            {
                RemComp<CarryingComponent>(carrier);
                RemComp<CarryingSlowdownComponent>(carrier);
                _movementSpeed.RefreshMovementSpeedModifiers(carrier);
                return;
            }

            while (carrier.Comp.Carried.Count > 0)
            {
                using var enumerator = carrier.Comp.Carried.GetEnumerator();
                enumerator.MoveNext();
                DropCarried(carrier, enumerator.Current);
            }

            if (TryComp<CarryingComponent>(carrier, out var carrying))
                FinalizeCarryingState((carrier, carrying));
        }

        private bool TrySpawnCarryVirtualItems(Entity<CarriableComponent> carried, EntityUid carrier)
        {
            for (var i = 0; i < carried.Comp.FreeHandsRequired; i++)
            {
                if (_virtualItemSystem.TrySpawnVirtualItemInHand(carried, carrier))
                    continue;

                _virtualItemSystem.DeleteInHandsMatching(carrier, carried);
                return false;
            }

            return true;
        }

        private void RecalculateCarrySlowdown(Entity<CarryingComponent> carrying)
        {
            PruneCarried(carrying);

            if (carrying.Comp.Carried.Count == 0)
            {
                RemComp<CarryingSlowdownComponent>(carrying);
                _movementSpeed.RefreshMovementSpeedModifiers(carrying.Owner);
                return;
            }

            var walkModifier = 1f;
            var sprintModifier = 1f;

            foreach (var carried in carrying.Comp.Carried)
            {
                var massRatio = _contests.MassContest(carrying.Owner, carried, true);
                var massRatioSq = massRatio * massRatio;
                var modifier = 1 - 0.15f / massRatioSq;
                modifier = Math.Max(0.1f, modifier);
                walkModifier *= modifier;
                sprintModifier *= modifier;
            }

            var slowdownComp = EnsureComp<CarryingSlowdownComponent>(carrying);
            _slowdown.SetModifier(carrying.Owner, walkModifier, sprintModifier, slowdownComp);
        }

        private void PruneCarried(Entity<CarryingComponent> carrying)
        {
            carrying.Comp.Carried.RemoveWhere(uid => TerminatingOrDeleted(uid));

            if (carrying.Comp.Carried.Count != 0)
                return;

            RemCompDeferred<CarryingComponent>(carrying);
            RemComp<CarryingSlowdownComponent>(carrying);
            _movementSpeed.RefreshMovementSpeedModifiers(carrying.Owner);
        }

        private void FinalizeCarryingState(Entity<CarryingComponent> carrying)
        {
            if (carrying.Comp.Carried.Count == 0)
            {
                RemComp<CarryingComponent>(carrying);
                RemComp<CarryingSlowdownComponent>(carrying);
            }
            else
            {
                RecalculateCarrySlowdown(carrying);
            }

            _movementSpeed.RefreshMovementSpeedModifiers(carrying);
        }
        // Exodus-end

        public bool CanCarry(EntityUid carrier, EntityUid carried, CarriableComponent? carriedComp = null)
        {
            if (!Resolve(carried, ref carriedComp, false)
                || carriedComp.CancelToken != null
                || !HasComp<MapGridComponent>(Transform(carrier).ParentUid)
                || HasComp<BeingCarriedComponent>(carrier)
                || HasComp<BeingCarriedComponent>(carried)
                || !TryComp<HandsComponent>(carrier, out var hands))
                return false;

            // Exodus-begin: multi-carry
            if (TryComp<CarryingComponent>(carrier, out var carrying))
                PruneCarried((carrier, carrying));

            if (CountFreeHands(hands) < carriedComp.FreeHandsRequired)
                return false;
            // Exodus-end

            return true;
        }

        // Exodus-begin: multi-carry
        private static int CountFreeHands(HandsComponent hands)
        {
            var free = 0;

            foreach (var hand in hands.Hands.Values)
            {
                if (hand.IsEmpty)
                    free++;
            }

            return free;
        }
        // Exodus-end

        public override void Update(float frameTime)
        {
            _pendingDrops.Clear(); // Exodus multi-carry

            var query = EntityQueryEnumerator<BeingCarriedComponent>();
            while (query.MoveNext(out var carried, out var comp))
            {
                var carrier = comp.Carrier;

                if (carried is not { Valid: true })
                    continue;

                // Exodus-begin: multi-carry
                if (TerminatingOrDeleted(carrier))
                {
                    _pendingDrops.Add((EntityUid.Invalid, carried));
                    continue;
                }
                // Exodus-end

                // SOMETIMES - when an entity is inserted into disposals, or a cryosleep chamber - it can get re-parented without a proper reparent event
                // when this happens, it needs to be dropped because it leads to weird behavior
                // Exodus-begin: multi-carry
                var xform = Transform(carried);
                if (xform.ParentUid != carrier)
                {
                    _pendingDrops.Add((carrier, carried));
                    continue;
                }
                // Exodus-end

                // Make sure the carried entity is always centered relative to the carrier, as gravity pulls can offset it otherwise
                if (!xform.LocalPosition.Equals(Vector2.Zero))
                {
                    xform.LocalPosition = Vector2.Zero;
                }
            }
            query.Dispose();

            // Exodus-begin: multi-carry
            foreach (var (carrier, carried) in _pendingDrops)
            {
                if (carrier.IsValid())
                    DropCarried(carrier, carried);
                else
                    CleanupCarriedVictim(carried);
            }
            // Exodus-end
        }
    }
}
