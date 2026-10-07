// Exodus-begin: configurable part curves for industrial lathes.
using Content.Server._Exodus.Construction;
using Content.Server.Construction;
using Content.Shared._Exodus.Lathe;
using Content.Shared.Lathe;

namespace Content.Server.Lathe;

public sealed partial class LatheSystem
{
    [Dependency] private MachinePartUpgradeSystem _partUpgrades = default!;

    private void ApplyPartMultiplierOverrides(Entity<LatheComponent> ent, RefreshPartsEvent args)
    {
        if (!TryComp<LathePartMultipliersComponent>(ent, out var multipliers))
            return;

        ent.Comp.FinalTimeMultiplier = ent.Comp.TimeMultiplier *
            _partUpgrades.GetMultiplier(args.Parts, ent.Comp.MachinePartPrintSpeed, multipliers.PrintTimeMultipliers);
        ent.Comp.FinalMaterialUseMultiplier = ent.Comp.MaterialUseMultiplier *
            _partUpgrades.GetMultiplier(args.Parts, ent.Comp.MachinePartMaterialUse, multipliers.MaterialUseMultipliers);
    }
}
// Exodus-end
