// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using System.Linq;
using Content.Server.Silicons.Laws;
using Content.Shared.Actions;
using Content.Shared.Silicons.Laws;
using Content.Shared.Silicons.Laws.Components;
using Content.Shared._EinsteinEngines.Language.Components;
using Content.Shared._EinsteinEngines.Language.Events;
using Content.Server._EinsteinEngines.Language;
using Content.Shared._Exodus.Virology.Behaviors;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Behaviors;

public sealed partial class VirusSynthificationSystem : EntitySystem
{
    [Dependency] private SiliconLawSystem _siliconLaw = default!;
    [Dependency] private LanguageSystem _language = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VirusSynthificationComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<VirusSynthificationComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VirusSynthificationComponent, GetSiliconLawsEvent>(OnGetLaws);
        SubscribeLocalEvent<VirusSynthificationComponent, DetermineEntityLanguagesEvent>(OnDetermineLanguages,
            after: new[] { typeof(LanguageSystem), typeof(TranslatorSystem) });
    }

    private void OnStartup(Entity<VirusSynthificationComponent> ent, ref ComponentStartup args)
    {
        var lawsets = _prototype.EnumeratePrototypes<SiliconLawsetPrototype>()
            .Where(lawset => !ent.Comp.ExcludedLawsets.Contains(lawset.ID))
            .OrderBy(lawset => lawset.ID, StringComparer.Ordinal)
            .ToList();
        if (lawsets.Count > 0)
        {
            // deterministic per carrier, so a re-grant (stage change) reproduce same lawset
            var rng = new Random(ent.Owner.GetHashCode());
            ent.Comp.RolledLawset = lawsets[rng.Next(lawsets.Count)].ID;
        }

        _actions.AddAction(ent.Owner, ref ent.Comp.LawsActionEntity, ent.Comp.LawsAction);
        if (!_ui.HasUi(ent.Owner, SiliconLawsUiKey.Key))
        {
            _ui.SetUi(ent.Owner, SiliconLawsUiKey.Key, new InterfaceData("SiliconLawBoundUserInterface", requireInputValidation: false));
            ent.Comp.AddedLawsUi = true;
        }

        if (TryComp<LanguageSpeakerComponent>(ent, out var language))
        {
            ent.Comp.OriginalSelected = language.CurrentLanguage;
            _language.UpdateEntityLanguages((ent.Owner, language));
        }
    }

    private void OnShutdown(Entity<VirusSynthificationComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.LawsActionEntity);
        if (ent.Comp.AddedLawsUi)
            _ui.CloseUi(ent.Owner, SiliconLawsUiKey.Key);

        if (Terminating(ent.Owner))
            return;

        ent.Comp.Reverting = true;
        _language.UpdateEntityLanguages(ent.Owner);
        if (ent.Comp.OriginalSelected is { } selected)
            _language.SetLanguage(ent.Owner, selected);
    }

    private void OnDetermineLanguages(Entity<VirusSynthificationComponent> ent, ref DetermineEntityLanguagesEvent args)
    {
        if (ent.Comp.Reverting)
            return;

        // Override effective languages, preserving innate knowledge and any translators for recovery.
        if (ent.Comp.OnlyBinary)
        {
            args.SpokenLanguages.Clear();
            args.UnderstoodLanguages.Clear();
        }

        if (ent.Comp.AddBinary || ent.Comp.OnlyBinary)
        {
            args.SpokenLanguages.Add(ent.Comp.Binary);
            args.UnderstoodLanguages.Add(ent.Comp.Binary);
        }
    }

    private void OnGetLaws(Entity<VirusSynthificationComponent> ent, ref GetSiliconLawsEvent args)
    {
        if (args.Handled || ent.Comp.RolledLawset is not { } lawset)
            return;

        args.Laws = _siliconLaw.GetLawset(lawset);
        args.Handled = true;
    }
}
