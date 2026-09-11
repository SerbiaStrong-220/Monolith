// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Prototypes;
using Content.Shared._Exodus.Virology.Behaviors;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Behaviors;

public sealed partial class BreathInversionSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private MetabolizerSystem _metabolizer = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BreathInversionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BreathInversionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<LungComponent, OrganAddedToBodyEvent>(OnLungAdded);
        SubscribeLocalEvent<LungComponent, OrganRemovedFromBodyEvent>(OnLungRemoved);
    }

    private void OnStartup(Entity<BreathInversionComponent> ent, ref ComponentStartup args)
    {
        foreach (var (organ, _) in _body.GetBodyOrgans(ent.Owner))
        {
            if (HasComp<LungComponent>(organ))
                Apply(ent, organ);
        }
    }

    private void OnShutdown(Entity<BreathInversionComponent> ent, ref ComponentShutdown args)
    {
        foreach (var (lung, original) in ent.Comp.Original)
        {
            if (!Terminating(lung) && TryComp<MetabolizerComponent>(lung, out var metabolizer))
                _metabolizer.SetMetabolizerTypes((lung, metabolizer), original);
        }

        ent.Comp.Original.Clear();
    }

    private void OnLungAdded(Entity<LungComponent> ent, ref OrganAddedToBodyEvent args)
    {
        if (TryComp<BreathInversionComponent>(args.Body, out var inversion))
            Apply((args.Body, inversion), ent.Owner);
    }

    private void OnLungRemoved(Entity<LungComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (TryComp<BreathInversionComponent>(args.OldBody, out var inversion)
            && inversion.Original.Remove(ent.Owner, out var original)
            && TryComp<MetabolizerComponent>(ent, out var metabolizer))
            _metabolizer.SetMetabolizerTypes((ent.Owner, metabolizer), original);
    }

    private void Apply(Entity<BreathInversionComponent> ent, EntityUid lung)
    {
        if (ent.Comp.Original.ContainsKey(lung)
            || !TryComp<MetabolizerComponent>(lung, out var metabolizer)
            || metabolizer.MetabolizerTypes is not { } types)
            return;

        var inverted = new HashSet<ProtoId<MetabolizerTypePrototype>>();
        foreach (var type in types)
            inverted.Add(ent.Comp.InvertTo.GetValueOrDefault(type, type));

        if (types.SetEquals(inverted))
            return;

        ent.Comp.Original.Add(lung, [.. types]);
        _metabolizer.SetMetabolizerTypes((lung, metabolizer), inverted);
    }
}
