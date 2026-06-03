namespace Content.Server._Exodus.Implants;

/// <summary>
/// Schedules a delayed trigger on the implant when the implanted body enters Dead state.
/// Resets if the body leaves Dead before the delay expires (e.g. revival). After the trigger
/// fires the scheduling slot becomes free again, so subsequent Dead-transitions can rearm the
/// timer — keep this in mind if you reuse the component on something that can repeatedly die.
/// </summary>
[RegisterComponent, Access(typeof(DelayedTriggerOnMobstateChangeSystem))]
public sealed partial class DelayedTriggerOnMobstateChangeComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Game time at which the trigger should fire. <see cref="TimeSpan.Zero"/> means
    /// "not scheduled" (initial state, after reset, or after the trigger fired).
    /// </summary>
    [ViewVariables]
    public TimeSpan TriggerAt = TimeSpan.Zero;
}
