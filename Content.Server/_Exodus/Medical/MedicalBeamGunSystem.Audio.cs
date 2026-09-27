using Content.Shared._Exodus.Medical;

namespace Content.Server._Exodus.Medical;

public sealed partial class MedicalBeamGunSystem
{
    private void StartTreatmentSound(Entity<MedicalBeamActiveComponent> beam, MedicalBeamGunComponent gun)
    {
        if (!beam.Comp.Running || TerminatingOrDeleted(beam) || beam.Comp.AudioStream != null || gun.HealingSound == null)
            return;

        beam.Comp.AudioStream = _audio.PlayPvs(gun.HealingSound, beam,
            gun.HealingSound.Params.WithLoop(true))?.Entity;
        _audio.PlayPvs(gun.StartSound, beam);
    }

    private void StopTreatmentSound(Entity<MedicalBeamActiveComponent> beam)
    {
        if (beam.Comp.AudioStream == null)
            return;

        beam.Comp.AudioStream = _audio.Stop(beam.Comp.AudioStream);
        if (!TerminatingOrDeleted(beam) && TryComp<MedicalBeamGunComponent>(beam, out var gun))
            _audio.PlayPvs(gun.StopSound, beam);
    }
}
