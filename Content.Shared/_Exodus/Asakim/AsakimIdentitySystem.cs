using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Asakim;

public sealed class AsakimIdentitySystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> AsakimBrainTag = "AsakimBrain";

    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    public bool HasAsakimBrain(EntityUid uid)
    {
        if (!TryComp<BodyComponent>(uid, out var body))
            return false;

        foreach (var (organ, _) in _body.GetBodyOrgans(uid, body))
        {
            if (_tag.HasTag(organ, AsakimBrainTag))
                return true;
        }

        return false;
    }
}
