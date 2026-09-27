using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Materials;

namespace Content.Client._Exodus.Mining.Pipes;

public sealed class MiningPipeNetSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MiningPipeNetworkMemberComponent, GetStoredMaterialsEvent>(OnGetMaterials);
    }

    private void OnGetMaterials(Entity<MiningPipeNetworkMemberComponent> ent, ref GetStoredMaterialsEvent args)
    {
        if (args.LocalOnly)
            return;

        foreach (var (material, amount) in ent.Comp.RemoteMaterials)
            args.Materials[material] = (int)Math.Min(int.MaxValue, (long)args.Materials.GetValueOrDefault(material) + amount);
    }
}
