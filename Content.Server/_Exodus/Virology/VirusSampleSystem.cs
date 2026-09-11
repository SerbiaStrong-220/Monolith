// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Labels.EntitySystems;
using Content.Shared._Exodus.Virology;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirusSampleSystem : EntitySystem
{
    [Dependency] private VirologySystem _virology = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private SharedLabelSystem _labels = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VirusSampleComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<VirusSampleComponent> ent, ref MapInitEvent args)
    {
        if (_virology.BuildDescriptor(ent.Comp.Virus) is not { } descriptor)
            return;

        if (!_solutionContainer.EnsureSolutionEntity((ent.Owner, null), ent.Comp.Solution, out var soln, ent.Comp.Amount))
            return;

        var virusData = new VirusData { Viruses = [descriptor] };
        _solutionContainer.TryAddReagent(soln.Value, ent.Comp.Carrier, ent.Comp.Amount, out _, data: [virusData]);
        _labels.Label(ent.Owner, _virology.ResolveName(descriptor));
    }
}
