// Exodus-begin: gradually reveal local speech without changing the received chat message.
using System.Text;
using Content.Client._Exodus.UserInterface.Controls;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Chat;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Chat.UI;

public abstract partial class SpeechBubble
{
    private const double CharactersPerSecond = 20;
    private static readonly TimeSpan MaximumTextRevealDuration = TimeSpan.FromSeconds(3);

    private TypewriterRichTextLabel? _typewriterLabel;
    private TimeSpan _textRevealStart;

    /// <summary>
    /// Time reserved for revealing speech before the normal reading lifetime starts.
    /// Also prevents the next queued bubble from starting while this one is still revealing.
    /// </summary>
    public TimeSpan TextRevealDuration { get; private set; }

    protected RichTextLabel CreateSpeechTextLabel(ChatMessage message, Color? fontColor)
    {
        var formatted = ExtractAndFormatSpeechSubstring(message, "BubbleContent", fontColor);
        if (!ConfigManager.GetCVar(EXCVars.ChatTypewriterEnabled) ||
            message.Channel is not (ChatChannel.Local or ChatChannel.Whisper))
        {
            var label = new RichTextLabel { MaxWidth = SpeechMaxWidth };
            label.SetMessage(formatted);
            return label;
        }

        var characters = 0;
        foreach (var node in formatted)
        {
            if (!node.IsPlainText || node.Value.StringValue is not { } text)
                continue;

            foreach (var rune in text.EnumerateRunes())
            {
                if (!Rune.IsWhiteSpace(rune))
                    characters++;
            }
        }

        TextRevealDuration = TimeSpan.FromSeconds(Math.Min(characters / CharactersPerSecond, MaximumTextRevealDuration.TotalSeconds));
        _textRevealStart = _timing.RealTime;
        _typewriterLabel = new TypewriterRichTextLabel { MaxWidth = SpeechMaxWidth };
        _typewriterLabel.SetMessage(formatted);
        return _typewriterLabel;
    }

    private void UpdateTextReveal()
    {
        if (_typewriterLabel == null || _typewriterLabel.RevealProgress >= 1f)
            return;

        _typewriterLabel.RevealProgress = TextRevealDuration > TimeSpan.Zero
            ? (float) Math.Clamp((_timing.RealTime - _textRevealStart).TotalSeconds / TextRevealDuration.TotalSeconds, 0, 1)
            : 1f;
    }

    private void CompleteTextReveal()
    {
        if (_typewriterLabel != null)
            _typewriterLabel.RevealProgress = 1f;
    }
}
// Exodus-end
