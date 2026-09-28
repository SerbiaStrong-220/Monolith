using Content.Shared._EinsteinEngines.Language;
using Content.Shared._EinsteinEngines.Language.Events;
using Content.Shared._Exodus.Language;
using Robust.Shared.Prototypes;

namespace Content.Server._EinsteinEngines.Language;

public sealed partial class LanguageSystem
{
    private bool IsAnatomicallyAllowed(EntityUid uid, ProtoId<LanguagePrototype> language) =>
        !TryComp<LanguageRestrictionComponent>(uid, out var restriction) || restriction.Allowed.Contains(language);

    private void RestrictLanguages(EntityUid uid, ref DetermineEntityLanguagesEvent args)
    {
        if (!TryComp<LanguageRestrictionComponent>(uid, out var restriction))
            return;

        args.SpokenLanguages.IntersectWith(restriction.Allowed);
        args.UnderstoodLanguages.IntersectWith(restriction.Allowed);
    }
}
