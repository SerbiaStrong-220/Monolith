using Content.Shared.Audio.Jukebox;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Exodus.MusicPlayerPortable;

public sealed partial class MusicPlayerPortableSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MusicPlayerPortableComponent, PowerCellSlotEmptyEvent>(OnPowerCellSlotEmpty);
        SubscribeLocalEvent<MusicPlayerPortableComponent, PowerCellChangedEvent>(OnPowerCellChanged);
        SubscribeLocalEvent<MusicPlayerPortableComponent, BoundUIOpenedEvent>(OnBUIOpen);
        SubscribeLocalEvent<MusicPlayerPortableComponent, BoundUIClosedEvent>(OnBUIClose);
        SubscribeLocalEvent<MusicPlayerPortableComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, MusicPlayerPortableComponent MPPlayer, MapInitEvent args)
    {
        MPPlayer.NextUpdate = _timing.CurTime + MPPlayer.UpdateInterval;
        _appearance.SetData(uid, MppItemVisuals.State, MppItemStates.Off);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var currentTime = _timing.CurTime;

        var query = EntityQueryEnumerator<MusicPlayerPortableComponent, JukeboxComponent, PowerCellDrawComponent>();
        while (query.MoveNext(out var ent, out var MPPlayer, out var jukebox, out var powerCellDrawComp))
        {
            if (MPPlayer.NextUpdate > currentTime)
                return;

            if (_ui.IsAnyUiOpen(ent))
            {
                MPPlayer.NextUpdate = currentTime + MPPlayer.UpdateInterval;
                return;
            }

            if (jukebox.AudioStream != null
                && Exists(jukebox.AudioStream.Value)
                && HasComp<MetaDataComponent>(jukebox.AudioStream.Value)
                && _audio.IsPlaying(jukebox.AudioStream.Value))
            {
                powerCellDrawComp.DrawRate = MPPlayer.DrawRate;
                _appearance.SetData(ent, MppItemVisuals.State, MppItemStates.On);
            }
            else
            {
                _appearance.SetData(ent, MppItemVisuals.State, MppItemStates.Off);
                powerCellDrawComp.DrawRate = 0f;
            }

            MPPlayer.NextUpdate = currentTime + MPPlayer.UpdateInterval;
        }

    }

    private void OnBUIClose(EntityUid uid, MusicPlayerPortableComponent MPPlayer, BoundUIClosedEvent args)
    {
        if (!TryComp<PowerCellDrawComponent>(uid, out var powerDrawComp))
            return;

        if (!TryComp<JukeboxComponent>(uid, out var jukebox)
            || jukebox.AudioStream == null
            || !Exists(jukebox.AudioStream.Value)
            || !_audio.IsPlaying(jukebox.AudioStream.Value))
        {
            powerDrawComp.DrawRate = 0f;
            _appearance.SetData(uid, MppItemVisuals.State, MppItemStates.Off);
            return;
        }

        _appearance.SetData(uid, MppItemVisuals.State, MppItemStates.On);
        powerDrawComp.DrawRate = MPPlayer.DrawRate;
    }

    private void OnBUIOpen(EntityUid uid, MusicPlayerPortableComponent MPPlayer, BoundUIOpenedEvent args)
    {
        if (!TryComp<PowerCellDrawComponent>(uid, out var powerDrawComp))
            return;

        _appearance.SetData(uid, MppItemVisuals.State, MppItemStates.On);
        powerDrawComp.DrawRate = MPPlayer.DrawRate;
    }

    private void OnPowerCellChanged(Entity<MusicPlayerPortableComponent> ent, ref PowerCellChangedEvent args)
    {
        if (!args.Ejected || !TryComp<JukeboxComponent>(ent, out var jukebox))
            return;

        StopAudio(ent.Owner, jukebox);
        CloseUI(ent.Owner);
    }

    private void OnPowerCellSlotEmpty(Entity<MusicPlayerPortableComponent> ent, ref PowerCellSlotEmptyEvent args)
    {
        _appearance.SetData(ent, MppItemVisuals.State, MppItemStates.Off);

        if (!TryComp<JukeboxComponent>(ent.Owner, out var jukebox))
            return;

        StopAudio(ent.Owner, jukebox);
        CloseUI(ent.Owner);
    }

    private void CloseUI(EntityUid ent)
    {
        if (!TryComp<UserInterfaceComponent>(ent, out var ui))
            return;

        _ui.CloseUis((ent, ui));
    }

    private void StopAudio(EntityUid ent, JukeboxComponent jukebox)
    {
        if (jukebox.AudioStream != null
            && Exists(jukebox.AudioStream.Value)
            && HasComp<MetaDataComponent>(jukebox.AudioStream.Value))
        {
            _audio.SetState(jukebox.AudioStream, AudioState.Stopped);
        }

        Dirty(ent, jukebox);
    }
}

[Serializable, NetSerializable]
public enum MppItemVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum MppItemStates : byte
{
    Off,
    On,
}
