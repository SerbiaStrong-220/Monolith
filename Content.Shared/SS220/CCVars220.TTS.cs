// (c) Space Exodus Team - EXDS-RL with CLA

using Robust.Shared.Configuration;

namespace Content.Shared.SS220.CCVars;

public sealed partial class CCVars220
{
    /// <summary>
    /// Master switch for receiving tts
    /// </summary>
    public static readonly CVarDef<bool> RecieveTTS =
        CVarDef.Create("tts.receive_tts", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Master switch for receiving tts
    /// </summary>
    public static readonly CVarDef<bool> PlayDifferentRadioTogether =
        CVarDef.Create("tts.play_different_radio_together", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Master switch for receiving tts
    /// </summary>
    public static readonly CVarDef<bool> PlayDifferentTalkingTogether =
        CVarDef.Create("tts.play_different_talk_together", true, CVar.CLIENTONLY | CVar.ARCHIVE);


    public static readonly CVarDef<bool> UseFFMpegProcessing =
        CVarDef.Create("tts.use_ffmpeg_processing", true, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Maximum entity capacity for tts queue
    /// </summary>
    public static readonly CVarDef<int> MaxQueuedPerEntity =
        CVarDef.Create("tts.max_queued_entity", 20, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Maximum of queued tts entities
    /// </summary>
    public static readonly CVarDef<int> MaxEntitiesQueued =
        CVarDef.Create("tts.max_entities_queued", 30, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Defines how long messages can be processed into audio by tts
    /// </summary>
    public static readonly CVarDef<int> MaxCharInTTSMessage =
        CVarDef.Create("tts.max_char_message", 100 * 2, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Defines how long messages can be processed into audio by tts
    /// </summary>
    public static readonly CVarDef<int> MaxCharInTTSAnnounceMessage =
        CVarDef.Create("tts.max_char_announce_message", 100 * 4, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Default volume setting of TTS sound
    /// </summary>
    public static readonly CVarDef<float> TTSVolume =
        CVarDef.Create("tts.volume", 0.50f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Default volume setting of TTS Radio sound
    /// </summary>
    public static readonly CVarDef<float> TTSRadioVolume =
        CVarDef.Create("tts.radio_volume", 0.50f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// URL of the TTS server API.
    /// </summary>
    public static readonly CVarDef<bool> TTSEnabled =
        CVarDef.Create("tts.enabled", false, CVar.SERVERONLY);

    /// <summary>
    /// URL of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> TTSApiUrl =
        CVarDef.Create("tts.api_url", "", CVar.SERVERONLY);

    /// <summary>
    /// Auth token of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> TTSApiToken =
        CVarDef.Create("tts.api_token", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Count of in-memory cached tts voice lines.
    /// </summary>
    public static readonly CVarDef<int> TTSMaxCache =
        CVarDef.Create("tts.max_cache", 250, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// TTS request timeout in seconds.
    /// </summary>
    public static readonly CVarDef<float> TTSRequestTimeout =
        CVarDef.Create("tts.timeout", 5f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// VoiceId for Announcement TTS
    /// </summary>
    public static readonly CVarDef<string> TTSAnnounceVoiceId =
        CVarDef.Create("tts.announce_voice", "glados", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Default volume setting of TTS Announce sound
    /// </summary>
    public static readonly CVarDef<float> TTSAnnounceVolume =
        CVarDef.Create("tts.announce_volume", 0.50f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
