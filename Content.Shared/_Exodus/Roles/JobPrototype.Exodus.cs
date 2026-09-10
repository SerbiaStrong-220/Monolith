using Robust.Shared.Prototypes;

namespace Content.Shared.Roles;

public sealed partial class JobPrototype
{
    /// <summary>
    /// Role entry price, paid from the character's bank account on manual entry or high-priority assignment.
    /// Zero keeps the normal, free job spawning flow.
    /// </summary>
    [DataField]
    public int EntryPrice { get; private set; }

    /// <summary>
    /// Antagonist role ban that also prevents purchasing this job.
    /// </summary>
    [DataField]
    public ProtoId<AntagPrototype>? RequiredAntag { get; private set; }
}
