using Content.Shared.Mobs;

namespace Content.Client._Exodus.MedicalTracking;

public static class MedicalTrackingDisplay
{
    public const int StaleSignalSeconds = 15;

    public static int SignalAge(TimeSpan sample, TimeSpan now) => Math.Max(0, (int) (now - sample).TotalSeconds);

    public static Color SignalColor(TimeSpan sample, TimeSpan now) => SignalAge(sample, now) >= StaleSignalSeconds
        ? MedicalTrackingUiTheme.Warning
        : MedicalTrackingUiTheme.Muted;

    public static string ShortStatus(MobState state) => state == MobState.Critical
        ? Loc.GetString("medical-tracking-critical-short")
        : Status(state);

    public static string Status(MobState state) => Loc.GetString(state switch
    {
        MobState.Alive => "medical-tracking-alive",
        MobState.Critical => "medical-tracking-critical",
        _ => "medical-tracking-dead",
    });

    public static Color StatusColor(MobState state) => state switch
    {
        MobState.Alive => MedicalTrackingUiTheme.Alive,
        MobState.Critical => MedicalTrackingUiTheme.Warning,
        _ => MedicalTrackingUiTheme.Danger,
    };

    public static int Priority(MobState state) => state switch
    {
        MobState.Critical => 0,
        MobState.Dead => 1,
        _ => 2,
    };
}
