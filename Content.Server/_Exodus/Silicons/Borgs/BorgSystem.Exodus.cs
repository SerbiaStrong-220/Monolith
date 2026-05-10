using Content.Server._Exodus.Silicons.Borgs;
using Content.Shared.Verbs;

namespace Content.Server.Silicons.Borgs;

public sealed partial class BorgSystem
{
    private void InitializeExodus()
    {
        SubscribeLocalEvent<BorgModuleItemComponent, GetVerbsEvent<AlternativeVerb>>(OnBorgItemGetVerbs);
    }

    private void OnBorgItemGetVerbs(Entity<BorgModuleItemComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!HasComp<BorgModuleStackRechargerComponent>(ent.Comp.ModuleUid))
            return;

        args.Verbs.RemoveWhere(v => v.Category == VerbCategory.Split);
    }
}
