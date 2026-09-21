using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared.Humanoid;
using Content.Shared.SS220.TTS;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.SS220.TTS;

public sealed partial class TTSSystem
{
    [Dependency] private SharedHumanoidAppearanceSystem _humanoidAppearance = default!;

    private void InitializeVoicePools()
    {
        SubscribeLocalEvent<TTSComponent, ProfileLoadFinishedEvent>(OnVoicePoolProfileLoaded);
        SubscribeLocalEvent<TTSComponent, SexChangedEvent>(OnVoicePoolSexChanged);
    }

    private void OnVoicePoolProfileLoaded(Entity<TTSComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        UpdateSexMatchedVoice(ent);
    }

    private void OnVoicePoolSexChanged(Entity<TTSComponent> ent, ref SexChangedEvent args)
    {
        UpdateSexMatchedVoice(ent);
    }

    private void UpdateSexMatchedVoice(Entity<TTSComponent> ent)
    {
        if (!_prototypeManager.TryIndex(ent.Comp.RandomVoicesList, out var pool)
            || !pool.MatchSex
            || !TryComp<HumanoidAppearanceComponent>(ent, out var humanoid))
        {
            return;
        }

        SetSexMatchedVoice((ent.Owner, ent.Comp, humanoid), pool);
    }

    /// <summary>
    /// Randomizes ordinary pools once on map initialization. Sex-matched humanoid pools also
    /// handle profile loading, so neither MapInit handler order nor later profiles can bypass them.
    /// </summary>
    private void SetRandomVoice(Entity<TTSComponent> ent)
    {
        if (!_prototypeManager.TryIndex(ent.Comp.RandomVoicesList, out var pool))
            return;

        if (pool.MatchSex && TryComp<HumanoidAppearanceComponent>(ent, out var humanoid))
        {
            SetSexMatchedVoice((ent.Owner, ent.Comp, humanoid), pool);
            return;
        }

        if (pool.VoicesList.Count == 0)
        {
            Log.Warning($"TTS voice pool {pool.ID} is empty.");
            return;
        }

        ent.Comp.VoicePrototypeId = _random.Pick(pool.VoicesList);
        Dirty(ent);
    }

    private void SetSexMatchedVoice(Entity<TTSComponent, HumanoidAppearanceComponent> ent, RandomVoicesListPrototype pool)
    {
        var (uid, tts, humanoid) = ent;
        ProtoId<TTSVoicePrototype>? selected = null;
        var matches = 0;

        foreach (var voiceId in pool.VoicesList)
        {
            if (!_prototypeManager.TryIndex(voiceId, out var voice)
                || humanoid.Sex != Sex.Unsexed && voice.Sex != humanoid.Sex)
            {
                continue;
            }

            // Keep a valid saved/profile voice, including the voice copied to a clone.
            if (tts.VoicePrototypeId == voiceId)
            {
                selected = voiceId;
                break;
            }

            // Reservoir sampling selects uniformly without allocating a filtered list.
            matches++;
            if (_random.Next(matches) == 0)
                selected = voiceId;
        }

        if (selected is not { } selectedVoice)
        {
            Log.Warning($"TTS voice pool {pool.ID} has no voices for sex {humanoid.Sex}.");
            selectedVoice = SharedHumanoidAppearanceSystem.DefaultSexVoice[humanoid.Sex];
        }

        // Update both TTS and the appearance voice used by cloning.
        _humanoidAppearance.SetTTSVoice(uid, selectedVoice, humanoid);
    }
}
