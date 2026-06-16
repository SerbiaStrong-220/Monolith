using System.Linq;
using Content.Server._EinsteinEngines.Language;
using Content.Server.Chat.Systems;
using Content.Server.Emp;
using Content.Server.Radio.Components;
using Content.Server.Speech;
using Content.Shared._Mono.Radio;
using Content.Shared.Chat;
using Content.Shared.Inventory.Events;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;
using Content.Shared.SS220.TTS;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Radio.EntitySystems;

public sealed partial class HeadsetSystem : SharedHeadsetSystem
{
    [Dependency] private INetManager _netMan = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private LanguageSystem _language = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HeadsetComponent, RadioReceiveEvent>(OnHeadsetReceive);
        SubscribeLocalEvent<HeadsetComponent, EncryptionChannelsChangedEvent>(OnKeysChanged);

        SubscribeLocalEvent<WearingHeadsetComponent, EntitySpokeEvent>(OnSpeak);
    }

    private void OnKeysChanged(EntityUid uid, HeadsetComponent component, EncryptionChannelsChangedEvent args)
    {
        UpdateRadioChannels(uid, component, args.Component);
    }

    private void UpdateRadioChannels(EntityUid uid, HeadsetComponent headset, EncryptionKeyHolderComponent? keyHolder = null)
    {
        // make sure to not add ActiveRadioComponent when headset is being deleted
        if (!headset.Enabled || MetaData(uid).EntityLifeStage >= EntityLifeStage.Terminating)
            return;

        if (!Resolve(uid, ref keyHolder))
            return;

        if (keyHolder.Channels.Count == 0)
            RemComp<ActiveRadioComponent>(uid);
        else
            EnsureComp<ActiveRadioComponent>(uid).Channels = new(keyHolder.Channels.Select(c => c.Channel.Id)); // Exodus
    }

    private void OnSpeak(EntityUid uid, WearingHeadsetComponent component, EntitySpokeEvent args)
    {
        // Exodus-begin: support multiple active headsets
        var channel = args.Channel;
        if (channel == null)
            return;

        foreach (var headset in component.Headsets)
        {
            if (!CanSpeakOnChannel(headset, channel.ID))
                continue;

            _radio.SendRadioMessage(uid, args.Message, channel, headset);
            args.Channel = null; // prevent duplicate messages from other listeners.
            return;
        }
        // Exodus-end
    }

    protected override void OnGotEquipped(EntityUid uid, HeadsetComponent component, GotEquippedEvent args)
    {
        base.OnGotEquipped(uid, component, args);
        if (component.IsEquipped && component.Enabled)
        {
            AddWearingHeadset(args.Equipee, uid); // Exodus: support multiple active headsets
            UpdateRadioChannels(uid, component);
        }
    }

    protected override void OnGotUnequipped(EntityUid uid, HeadsetComponent component, GotUnequippedEvent args)
    {
        base.OnGotUnequipped(uid, component, args);
        RemComp<ActiveRadioComponent>(uid);
        RemoveWearingHeadset(args.Equipee, uid); // Exodus: support multiple active headsets
    }

    public void SetEnabled(EntityUid uid, bool value, HeadsetComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (component.Enabled == value)
            return;

        component.Enabled = value;
        Dirty(uid, component);

        if (!value)
        {
            RemCompDeferred<ActiveRadioComponent>(uid);

            if (component.IsEquipped)
                RemoveWearingHeadset(Transform(uid).ParentUid, uid); // Exodus: support multiple active headsets
        }
        else if (component.IsEquipped)
        {
            AddWearingHeadset(Transform(uid).ParentUid, uid); // Exodus: support multiple active headsets
            UpdateRadioChannels(uid, component);
        }
    }

    // Exodus-begin: support multiple active headsets
    private bool CanSpeakOnChannel(EntityUid headset, string channelId)
    {
        if (!TryComp(headset, out EncryptionKeyHolderComponent? keys))
            return false;

        foreach (var entry in keys.Channels)
        {
            if (entry.Channel == channelId && entry.CanSpeak)
                return true;
        }

        return false;
    }

    private void AddWearingHeadset(EntityUid wearer, EntityUid headset)
    {
        var wearing = EnsureComp<WearingHeadsetComponent>(wearer);
        if (!wearing.Headsets.Contains(headset))
            wearing.Headsets.Add(headset);
    }

    private void RemoveWearingHeadset(EntityUid wearer, EntityUid headset)
    {
        if (!TryComp<WearingHeadsetComponent>(wearer, out var wearing))
            return;

        wearing.Headsets.Remove(headset);

        if (wearing.Headsets.Count != 0)
            return;

        RemComp<WearingHeadsetComponent>(wearer);
    }
    // Exodus-end

    private void OnHeadsetReceive(EntityUid uid, HeadsetComponent component, ref RadioReceiveEvent args)
    {
        var parent = Transform(uid).ParentUid;

        if (TryComp(parent, out ActorComponent? actor))
        {
            // Einstein Engines - Language begin
            var canUnderstand = _language.CanUnderstand(Transform(uid).ParentUid, args.Language.ID);
            var msg = new MsgChatMessage
            {
                Message = canUnderstand ? args.OriginalChatMsg : args.LanguageObfuscatedChatMsg
            };
            _netMan.ServerSendMessage(msg, actor.PlayerSession.Channel);

            // Einstein Engines - Language end

            // Mono - Borers begin
            var ev = new RadioMessageHeardEvent(uid, msg, args.Channel);
            RaiseLocalEvent(Transform(uid).ParentUid, ref ev);
            // Mono - Borers end

            // Send radio noise event to client
            var radioNoiseEvent = new RadioNoiseEvent(GetNetEntity(uid), args.Channel.ID);
            RaiseNetworkEvent(radioNoiseEvent, actor.PlayerSession);

            // SS220 TTS-Radio begin
            if (parent != args.MessageSource && TryComp(args.MessageSource, out TTSComponent? _))
            {
                args.Receivers.Add(new(parent));
            }
            // SS220 TTS-Radio end
        }
    }
}
