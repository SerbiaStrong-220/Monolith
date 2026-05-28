using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;

namespace Content.Shared._Exodus.Asakim;

public sealed class AsakimIdentitySystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;

    public bool HasAsakimBrain(EntityUid uid)
    {
        if (!TryComp<BodyComponent>(uid, out var body))
            return false;

        foreach (var (organ, _) in _body.GetBodyOrgans(uid, body))
        {
            if (HasComp<AsakimBrainComponent>(organ))
                return true;
        }

        return false;
    }
}
