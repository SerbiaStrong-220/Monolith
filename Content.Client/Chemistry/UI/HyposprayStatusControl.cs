using Content.Client.Message;
using Content.Client.Stylesheets;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.Chemistry.UI;

public sealed class HyposprayStatusControl : Control
{
    private readonly Entity<HyposprayComponent> _parent;
    private readonly RichTextLabel _label;
    private readonly SharedSolutionContainerSystem _solutionContainers;

    private FixedPoint2 PrevVolume;
    private FixedPoint2 PrevMaxVolume;
    private bool PrevOnlyAffectsMobs;
    private bool PrevInjectOnly; // Exodus borg-paramedic-hypo

    public HyposprayStatusControl(Entity<HyposprayComponent> parent, SharedSolutionContainerSystem solutionContainers)
    {
        _parent = parent;
        _solutionContainers = solutionContainers;
        _label = new RichTextLabel { StyleClasses = { StyleNano.StyleClassItemStatus } };
        AddChild(_label);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_solutionContainers.TryGetSolution(_parent.Owner, _parent.Comp.SolutionName, out _, out var solution))
            return;

        // only updates the UI if any of the details are different than they previously were
        if (PrevVolume == solution.Volume
            && PrevMaxVolume == solution.MaxVolume
            && PrevOnlyAffectsMobs == _parent.Comp.OnlyAffectsMobs
            && PrevInjectOnly == _parent.Comp.InjectOnly) // Exodus borg-paramedic-hypo
            return;

        PrevVolume = solution.Volume;
        PrevMaxVolume = solution.MaxVolume;
        PrevOnlyAffectsMobs = _parent.Comp.OnlyAffectsMobs;
        PrevInjectOnly = _parent.Comp.InjectOnly; // Exodus borg-paramedic-hypo

        // Exodus-begin borg-paramedic-hypo
        var modeStringLocalized = Loc.GetString((_parent.Comp.InjectOnly, _parent.Comp.OnlyAffectsMobs) switch
        {
            (true, true) => "hypospray-mobs-only-inject-only-mode-text",
            (true, false) => "hypospray-all-mode-text",
            (false, true) => "hypospray-mobs-only-mode-text",
            (false, false) => "hypospray-all-mode-text",
        });
        // Exodus-end

        _label.SetMarkup(Loc.GetString("hypospray-volume-label",
            ("currentVolume", solution.Volume),
            ("totalVolume", solution.MaxVolume),
            ("modeString", modeStringLocalized)));
    }
}
