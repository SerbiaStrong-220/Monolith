using Content.Server.Research.Systems;
using Content.Server.Xenoarchaeology.Artifact;
using Content.Server.Power.Components;
using Content.Shared.Popups;
using Content.Shared.Xenoarchaeology.Equipment;
using Content.Shared.Xenoarchaeology.Equipment.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Server.Xenoarchaeology.Equipment;

/// <inheritdoc />
public sealed class ArtifactAnalyzerSystem : SharedArtifactAnalyzerSystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ResearchSystem _research = default!;
    [Dependency] private readonly XenoArtifactSystem _xenoArtifact = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AnalysisConsoleComponent, AnalysisConsoleExtractButtonPressedMessage>(OnExtractButtonPressed);
    }

    private void OnExtractButtonPressed(Entity<AnalysisConsoleComponent> ent, ref AnalysisConsoleExtractButtonPressedMessage args)
    {
        if (!TryGetArtifactFromConsole(ent, out var artifact))
            return;

        if (!_research.TryGetClientServer(ent, out var server, out var serverComponent))
            return;

        var sumResearch = 0;
        foreach (var node in _xenoArtifact.GetAllNodes(artifact.Value))
        {
            var research = _xenoArtifact.GetResearchValue(node);
            _xenoArtifact.SetConsumedResearchValue(node, node.Comp.ConsumedResearchValue + research);
            sumResearch += research;
        }

        if (sumResearch == 0)
            return;

        _research.ModifyServerPoints(server.Value, sumResearch, serverComponent);
        _audio.PlayPvs(ent.Comp.ExtractSound, artifact.Value);
        _popup.PopupEntity(Loc.GetString("analyzer-artifact-extract-popup"), artifact.Value, PopupType.Large);
    }

    // Frontier: reduce analyzer load when not running
    /*private void SetPowerSwitch(ArtifactAnalyzerComponent analyzer, ApcPowerReceiverComponent apc, bool state)
    {
        if (state)
            apc.Load = analyzer.OriginalLoad;
        else
            apc.Load = 1;
    }*/
    // End Frontier

    //MONO: Upgradeable scan speed
    /*private void OnRefreshParts(EntityUid uid, ArtifactAnalyzerComponent component, RefreshPartsEvent args)
    {
        var rating = args.PartRatings[component.MachinePartDuration];
        component.AnalysisDuration = TimeSpan.FromSeconds(component.BaseAnalysisDuration.TotalSeconds * MathF.Pow(component.PartRatingDurationMultiplier, rating - 1));
    } */

    /*private void OnUpgradeExamine(EntityUid uid, ArtifactAnalyzerComponent component, ref UpgradeExamineEvent args)
    {
        var displaypercent = (float)(component.AnalysisDuration.TotalSeconds / component.BaseAnalysisDuration.TotalSeconds);

        args.AddPercentageUpgrade("artifact-analyzer-upgrade-duration", displaypercent);
    }*/
    //Mono end
}

