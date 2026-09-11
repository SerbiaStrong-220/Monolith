// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Server.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared._Exodus.Virology;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;

    private void InitializeBlood()
    {
        SubscribeLocalEvent<VirusHolderComponent, VirusContentsChangedEvent>(OnContentsChanged);
        SubscribeLocalEvent<VirusHolderComponent, GetBloodDataEvent>(OnGetBloodData);
    }

    private void OnGetBloodData(Entity<VirusHolderComponent> ent, ref GetBloodDataEvent args)
    {
        if (BuildData(ent) is { } data)
            args.Data.Add(data);
    }

    private void OnContentsChanged(Entity<VirusHolderComponent> ent, ref VirusContentsChangedEvent args)
    {
        if (!TryComp<BloodstreamComponent>(ent, out var blood)
            || !_solutionContainer.ResolveSolution(ent.Owner, blood.BloodSolutionName, ref blood.BloodSolution, out var solution))
            return;

        var virusData = BuildData(ent);
        for (var i = 0; i < solution.Contents.Count; i++)
        {
            var quantity = solution.Contents[i];
            if (quantity.Reagent.Prototype != blood.BloodReagent)
                continue;

            // Solutions may share reagent metadata after splitting. Replace it without modifying other samples or donor DNA.
            var data = new List<ReagentData>();
            if (quantity.Reagent.Data is { } previous)
            {
                foreach (var entry in previous)
                {
                    if (entry is not VirusData)
                        data.Add(entry.Clone());
                }
            }

            if (virusData != null)
                data.Add(virusData.Clone());

            solution.Contents[i] = new ReagentQuantity(new ReagentId(quantity.Reagent.Prototype, data), quantity.Quantity);
        }

        _solutionContainer.UpdateChemicals(blood.BloodSolution!.Value);
    }

    private VirusData? BuildData(Entity<VirusHolderComponent> ent)
    {
        var descriptors = new List<VirusDescriptor>();
        foreach (var strain in EnumerateStrains(ent.Comp))
            descriptors.Add(ToDescriptor(strain));

        return descriptors.Count > 0 ? new VirusData { Viruses = descriptors } : null;
    }
}
