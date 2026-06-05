namespace Content.Server._Exodus.Implants;

/// <summary>
/// Schedules a delayed trigger on the implant when the implanted body enters Dead state.
///
/// The mob state is checked at TWO points by design:
/// 1. On the relayed <see cref="Content.Shared.Mobs.MobStateChangedEvent"/> — entering Dead arms
///    the timer (sets <see cref="TriggerAt"/>), leaving Dead disarms it (back to
///    <see cref="TimeSpan.Zero"/>).
/// 2. In the system Update, when <see cref="TriggerAt"/> elapses — the body's state is verified
///    again, and if it is no longer Dead at that moment the trigger is cancelled.
///
/// This double guard exists because the proper place would be a single cancellable attempt event,
/// which the engine does not currently expose for this case; the second check is the safety net.
///
/// After the trigger fires (or is cancelled) <see cref="TriggerAt"/> goes back to
/// <see cref="TimeSpan.Zero"/>, so subsequent Dead-transitions can rearm the timer — keep this
/// in mind if you reuse the component on something that can repeatedly die.
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
