// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Silicons.Laws;
using Content.Shared._EinsteinEngines.Language;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Virology.Behaviors;

[RegisterComponent]
public sealed partial class VirusSynthificationComponent : Component
{
    /// <summary>Language granted.</summary>
    [DataField]
    public ProtoId<LanguagePrototype> Binary = "RobotTalk";

    /// <summary>Action that opens laws screen.</summary>
    [DataField]
    public EntProtoId LawsAction = "ActionViewLaws";

    /// <summary>Add language this stage.</summary>
    [DataField]
    public bool AddBinary;

    /// <summary>Clear host's other languages, leaving only added(final stage).</summary>
    [DataField]
    public bool OnlyBinary;

    [ViewVariables]
    public EntityUid? LawsActionEntity;

    /// <summary>Blacklisted lawsets.</summary>
    [DataField]
    public HashSet<ProtoId<SiliconLawsetPrototype>> ExcludedLawsets = new()
    {
        "XenoborgLawset",
        "Ninja",
        "AntimovLawset",
    };

    /// <summary>Lawset rolled deterministically, so a re-grant same lawset.</summary>
    [ViewVariables]
    public ProtoId<SiliconLawsetPrototype>? RolledLawset;

    [ViewVariables]
    public bool AddedLawsUi;

    /// <summary>Disables language overrides while restoring the host's languages.</summary>
    [ViewVariables]
    public bool Reverting;

    /// <summary>Language selected before we took over, re-selected on cure if still available.</summary>
    [ViewVariables]
    public ProtoId<LanguagePrototype>? OriginalSelected;
}
