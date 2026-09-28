using Content.Shared.Mobs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;

namespace Content.Server._Exodus.MedicalTracking;

public sealed partial class MedicalTrackingSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;

    private void InitializeTabletAudio()
    {
        SubscribeLocalEvent<MedicalTrackingBodyComponent, MobStateChangedEvent>(OnTrackedBodyMobStateChanged);
    }

    private void OnTabletClosed(Entity<MedicalTrackingTabletComponent> ent, ref BoundUIClosedEvent args)
    {
        if (TerminatingOrDeleted(ent) || TerminatingOrDeleted(args.Actor) || !_access.IsAllowed(args.Actor, ent))
            return;

        _audio.PlayGlobal(ent.Comp.CloseSound, args.Actor);
    }

    private void OnTrackedBodyMobStateChanged(Entity<MedicalTrackingBodyComponent> ent, ref MobStateChangedEvent args)
    {
        // Ignore initialization, recovery and events relayed from another body.
        if (args.Target != ent.Owner || args.OldMobState == MobState.Invalid ||
            args.NewMobState <= args.OldMobState || Paused(ent) || !CanTrackBody((ent.Owner, args.Component)) ||
            Transform(ent).MapID == MapId.Nullspace ||
            !_implantQuery.TryGetComponent(ent.Comp.Implant, out var implant) ||
            TerminatingOrDeleted(ent.Comp.Implant) || !implant.TrackBody || implant.Body != ent.Owner)
            return;

        // Queue once per transition, even with every UI closed. MobState is ordered by severity.
        var query = EntityQueryEnumerator<MedicalTrackingTabletComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out var tablet, out var transform, out var metadata))
        {
            if (!metadata.EntityInitialized || metadata.EntityLifeStage >= EntityLifeStage.Terminating ||
                transform.MapID == MapId.Nullspace || !tablet.AlertSounds.ContainsKey(args.NewMobState))
                continue;

            if (tablet.PendingAlert is not { } pending || args.NewMobState > pending)
                tablet.PendingAlert = args.NewMobState;
        }
    }

    private void UpdateTabletAlert(Entity<MedicalTrackingTabletComponent> ent, TimeSpan now)
    {
        if (ent.Comp.PendingAlert is not { } pending || now < ent.Comp.NextAlert)
            return;

        ent.Comp.PendingAlert = null;
        if (TerminatingOrDeleted(ent) || !MetaData(ent).EntityInitialized ||
            Transform(ent).MapID == MapId.Nullspace || !ent.Comp.AlertSounds.TryGetValue(pending, out var sound))
            return;

        _audio.PlayPvs(sound, ent.Owner);
        var cooldown = ent.Comp.AlertCooldown > TimeSpan.Zero ? ent.Comp.AlertCooldown : TimeSpan.FromSeconds(3);
        // This is a cooldown from an actual notification, not a periodic sampling interval.
        ent.Comp.NextAlert = now + cooldown;
    }
}
