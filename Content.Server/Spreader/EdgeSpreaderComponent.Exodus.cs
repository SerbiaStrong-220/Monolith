// Exodus: optional per-entity spread intervals for slow-growing colonies.
namespace Content.Server.Spreader;

[AutoGenerateComponentPause]
public sealed partial class EdgeSpreaderComponent
{
    [DataField]
    public TimeSpan MinSpreadDelay;

    [DataField]
    public TimeSpan MaxSpreadDelay;

    [DataField, AutoPausedField]
    public TimeSpan NextSpread;
}
