namespace Content.Shared._Exodus.NPC.Pet;

[RegisterComponent]
public sealed partial class PetFollowerComponent : Component
{
    /// <summary>Distance (tiles) at which pet starts catching up.</summary>
    [DataField]
    public float FollowRange = 4f;

    /// <summary>Distance (tiles) pet settles if caught up to.</summary>
    [DataField]
    public float FollowCloseRange = 1f;

    /// <summary>Min/max idle time between follow re-evaluations, atm short so pet reacts quick.</summary>
    [DataField]
    public TimeSpan IdleTimeMin = TimeSpan.FromSeconds(0.3);

    [DataField]
    public TimeSpan IdleTimeMax = TimeSpan.FromSeconds(1.0);
}
