// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Nutrition;
using Content.Shared._Exodus.Nutrition;
using Content.Shared._Goobstation.EatToGrow;
using Content.Shared._Exodus.Virology;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    private readonly List<EntityUid> _expiredBuf = [];

    private void InitializeContamination()
    {
        SubscribeLocalEvent<VirusSusceptibleComponent, AfterEatingEvent>(OnIngested);
        SubscribeLocalEvent<VirusSusceptibleComponent, AfterDrinkEvent>(OnDrank);
        SubscribeLocalEvent<VirusContaminantComponent, EntityUnpausedEvent>(OnContaminantUnpaused);
    }

    private void OnContaminantUnpaused(Entity<VirusContaminantComponent> ent, ref EntityUnpausedEvent args)
    {
        foreach (var virus in ent.Comp.Viruses)
            virus.ExpiresAt += args.PausedTime;
    }

    /// <summary>Leaves a copy of a strain on the item, one per composition, (re)setting that strain's own timer.</summary>
    public void Contaminate(EntityUid target, VirusDescriptor descriptor)
    {
        var comp = EnsureComp<VirusContaminantComponent>(target);
        var identity = GetIdentity(descriptor);
        var expiresAt = _timing.CurTime + comp.Duration;

        // refresh only this strain's own deadline if it's already on the item
        foreach (var existing in comp.Viruses)
        {
            if (GetIdentity(existing.Descriptor) == identity)
            {
                existing.ExpiresAt = expiresAt;
                return;
            }
        }

        comp.Viruses.Add(new VirusContaminant { Descriptor = descriptor.Clone(), ExpiresAt = expiresAt });
    }

    private void OnIngested(Entity<VirusSusceptibleComponent> ent, ref AfterEatingEvent args)
    {
        InfectFromItem(ent.Owner, args.Food);
    }

    private void OnDrank(Entity<VirusSusceptibleComponent> ent, ref AfterDrinkEvent args)
    {
        InfectFromItem(ent.Owner, args.Drink);
    }

    private void InfectFromItem(EntityUid host, EntityUid item)
    {
        if (!TryComp<VirusContaminantComponent>(item, out var food))
            return;

        foreach (var contaminant in food.Viruses)
        {
            if (contaminant.ExpiresAt > _timing.CurTime && !IsBloodOnly(contaminant.Descriptor))
                AddVirus(host, contaminant.Descriptor);
        }
    }

    private void TickContamination()
    {
        var now = _timing.CurTime;
        _expiredBuf.Clear();
        var query = EntityQueryEnumerator<VirusContaminantComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // each strain fades on its own deadline; drop the ones that have run out
            for (var i = comp.Viruses.Count - 1; i >= 0; i--)
            {
                if (now >= comp.Viruses[i].ExpiresAt)
                    comp.Viruses.RemoveAt(i);
            }

            if (comp.Viruses.Count == 0)
                _expiredBuf.Add(uid);
        }

        foreach (var uid in _expiredBuf)
            RemComp<VirusContaminantComponent>(uid);
    }
}
