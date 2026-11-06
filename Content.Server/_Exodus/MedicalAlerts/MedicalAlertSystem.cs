using System;
using System.Diagnostics.CodeAnalysis;
using Content.Server.CartridgeLoader;
using Content.Server.Explosion.EntitySystems;
using Content.Server._Mono.Planets;
using Content.Server._NF.SectorServices;
using Content.Shared._Exodus.MedicalAlerts;
using Content.Shared.CartridgeLoader;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.MedicalAlerts;

public sealed partial class MedicalAlertSystem : EntitySystem
{
    public const int MaxEntries = 64;

    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<CartridgeLoaderComponent> _cartridgeLoaderQuery;

    public override void Initialize()
    {
        base.Initialize();
        _cartridgeLoaderQuery = GetEntityQuery<CartridgeLoaderComponent>();
        SubscribeLocalEvent<MedicalAlertOnTriggerComponent, TriggerEvent>(OnMedicalAlertTrigger);
        SubscribeLocalEvent<MedicalAlertRaisedEvent>(OnMedicalAlertRaised);
        InitializeUi();
    }

    private void OnMedicalAlertTrigger(Entity<MedicalAlertOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (!TryComp<SubdermalImplantComponent>(ent.Owner, out var implanted) || implanted.ImplantedEntity is not { } implantedUid)
            return;

        if (!TryComp<MobStateComponent>(implantedUid, out var mobState))
            return;

        var alertType = GetAlertType(mobState.CurrentState, mobState.PreviousState);
        if (alertType == null)
            return;

        ProtoId<SpeciesPrototype>? speciesId = null;
        if (TryComp<HumanoidAppearanceComponent>(implantedUid, out var humanoid))
            speciesId = humanoid.Species;

        var ownerXform = Transform(ent.Owner);
        var mapPos = ownerXform.MapPosition;
        var alert = new MedicalAlertRaisedEvent(
            implantedUid,
            alertType.Value,
            new Vector2i((int) mapPos.X, (int) mapPos.Y),
            ownerXform.GridUid,
            speciesId);
        RaiseLocalEvent(ref alert);
    }

    private void OnMedicalAlertRaised(ref MedicalAlertRaisedEvent args)
    {
        if (!TryGetAlertData(out var data))
            return;

        data.LastEntryId++;
        var entry = new MedicalAlertEntry(
            data.LastEntryId,
            args.AlertType,
            Identity.Name(args.Subject, EntityManager),
            args.SpeciesId,
            ResolveGridName(args.GridUid),
            args.Position,
            _timing.CurTime);

        AddEntry(data, entry);
        BroadcastAlertToCartridges(entry);
    }

    /// <summary>
    /// Appends an entry to the bounded log, dropping the oldest once <see cref="MaxEntries"/> is reached.
    /// </summary>
    private static void AddEntry(MedicalAlertDataComponent data, MedicalAlertEntry entry)
    {
        var current = data.Entries;
        if (current.Length < MaxEntries)
        {
            var grown = new MedicalAlertEntry[current.Length + 1];
            Array.Copy(current, grown, current.Length);
            grown[^1] = entry;
            data.Entries = grown;
            return;
        }

        var shifted = new MedicalAlertEntry[MaxEntries];
        Array.Copy(current, 1, shifted, 0, MaxEntries - 1);
        shifted[^1] = entry;
        data.Entries = shifted;
    }

    private string? ResolveGridName(EntityUid? gridUid)
    {
        if (gridUid is not { } grid)
            return null;

        // A bare (non-planet) map has no meaningful location name for medics.
        if (HasComp<MapComponent>(grid) && !HasComp<PlanetMapComponent>(grid))
            return null;

        return Name(grid);
    }

    public static MedicalAlertType? GetAlertType(MobState current, MobState previous)
    {
        return current switch
        {
            MobState.Dead => MedicalAlertType.Death,
            MobState.Critical when previous == MobState.Dead => MedicalAlertType.Revived,
            MobState.Alive when previous == MobState.Dead => MedicalAlertType.Revived,
            MobState.Critical => MedicalAlertType.Critical,
            _ => null,
        };
    }

    private bool TryGetAlertData([NotNullWhen(true)] out MedicalAlertDataComponent? data)
    {
        return TryComp(_sectorService.GetServiceEntity(), out data);
    }

    public MedicalAlertEntry[] GetAlertData()
    {
        if (!TryGetAlertData(out var data))
            return [];

        return data.Entries;
    }
}
