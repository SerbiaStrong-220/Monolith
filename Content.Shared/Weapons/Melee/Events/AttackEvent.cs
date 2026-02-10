using Content.Shared.Damage;
using Content.Shared.Inventory;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Melee.Events
{
    [Serializable, NetSerializable]
    public abstract class AttackEvent : EntityEventArgs
    {
        /// <summary>
        /// Coordinates being attacked.
        /// </summary>
        public readonly NetCoordinates Coordinates;

        protected AttackEvent(NetCoordinates coordinates)
        {
            Coordinates = coordinates;
        }
    }

    /// <summary>
    ///     Event raised on entities that have been attacked.
    /// </summary>
    public sealed class AttackedEvent : EntityEventArgs, IInventoryRelayEvent // Exodus | Make inventory relayed
    {
        public SlotFlags TargetSlots { get; } = SlotFlags.WITHOUT_POCKET;

        /// <summary>
        ///     Entity used to attack, for broadcast purposes.
        /// </summary>
        public EntityUid Used { get; }

        /// <summary>
        ///     Entity that triggered the attack.
        /// </summary>
        public EntityUid User { get; }

        // Exodus-Start | add target
        /// <summary>
        ///     Entity that have been attacked
        /// </summary>
        public EntityUid Target { get; }
        // Exodus-End

        /// <summary>
        ///     The original location that was clicked by the user.
        /// </summary>
        public EntityCoordinates ClickLocation { get; }

        public DamageSpecifier BonusDamage = new();

        public AttackedEvent(EntityUid used, EntityUid user, EntityCoordinates clickLocation, EntityUid target) // Exodus | add target
        {
            Used = used;
            User = user;
            ClickLocation = clickLocation;
            Target = target; // Exodus | add target
        }
    }
}
