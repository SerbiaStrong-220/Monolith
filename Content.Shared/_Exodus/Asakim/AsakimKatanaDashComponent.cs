using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Ninja.Systems;
using Content.Shared.Tag;

namespace Content.Shared._Exodus.Asakim;

[RegisterComponent]
public sealed partial class AsakimKatanaDashComponent : Component
{
    [DataField]
    public string RequiredOrganTag = "AsakimBrain";
}

public sealed class AsakimKatanaDashSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AsakimKatanaDashComponent, CheckDashEvent>(OnCheckDash);
    }

    private void OnCheckDash(Entity<AsakimKatanaDashComponent> ent, ref CheckDashEvent args)
    {
        if (!HasRequiredBrain(args.User, ent.Comp))
            args.Cancelled = true;
    }

    private bool HasRequiredBrain(EntityUid user, AsakimKatanaDashComponent component)
    {
        if (!TryComp<BodyComponent>(user, out var body))
            return false;

        foreach (var (organ, _) in _body.GetBodyOrgans(user, body))
        {
            if (_tag.HasTag(organ, component.RequiredOrganTag))
                return true;
        }

        return false;
    }
}
