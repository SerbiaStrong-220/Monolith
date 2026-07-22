using System.Collections.Generic; // Exodus multi-carry

namespace Content.Server.Carrying
{
    /// <summary>
    /// Added to an entity when they are carrying somebody.
    /// </summary>
    [RegisterComponent]
    public sealed partial class CarryingComponent : Component
    {
        // Exodus-begin: multi-carry
        /// <summary>
        /// Every entity currently being carried by this mob.
        /// </summary>
        public HashSet<EntityUid> Carried = new();
        // Exodus-end
    }
}
