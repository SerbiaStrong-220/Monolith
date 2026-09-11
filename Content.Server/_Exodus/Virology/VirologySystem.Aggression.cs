// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Effects;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    private void InitializeAggression()
    {
        SubscribeLocalEvent<VirusHolderComponent, VirusAggressionEffectEvent>(OnAggression);
    }

    private void OnAggression(Entity<VirusHolderComponent> ent, ref VirusAggressionEffectEvent args)
    {
        foreach (var strain in GetStrains(ent))
        {
            if (strain.Owner == args.Aggressor)
                continue;

            RemoveVirus(strain);
        }
    }
}
