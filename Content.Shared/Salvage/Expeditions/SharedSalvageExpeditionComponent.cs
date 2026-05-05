using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Salvage.Expeditions;

[NetworkedComponent]
public abstract partial class SharedSalvageExpeditionComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField("stage")]
    public ExpeditionStage Stage = ExpeditionStage.Added;

    // Exodus-add-exp-time-to-pda-start
    /// <summary>
    /// When the expeditions ends.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("endTime", customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan EndTime;
    // Exodus-add-exp-time-to-pda-end
}

[Serializable, NetSerializable]
public sealed class SalvageExpeditionComponentState : ComponentState
{
    public ExpeditionStage Stage;
    public TimeSpan EndTime; // Exodus-add-exp-time-to-pda
}
