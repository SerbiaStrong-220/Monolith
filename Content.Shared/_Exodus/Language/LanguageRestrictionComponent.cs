using Content.Shared._EinsteinEngines.Language;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Language;

/// <summary>Languages a creature's anatomy can support, including through translators.</summary>
[RegisterComponent]
public sealed partial class LanguageRestrictionComponent : Component
{
    [DataField(required: true)]
    public HashSet<ProtoId<LanguagePrototype>> Allowed = new();
}
