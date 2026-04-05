using Content.Shared._Exodus.Maths;
using Robust.Client.Animations;

namespace Content.Client._Exodus.Animations;

public sealed class AnimationTrackEasing : AnimationTrack
{
    public Easing Easing { get; set; }
    public TimeSpan Duration { get; set; }
    public Func<float, float>? CustomEasingFunc { get; set; }
    public AnimationTrack? Track { get; set; }

    public override (int KeyFrameIndex, float FramePlayingTime) InitPlayback()
    {
        return Track?.InitPlayback() ?? default;
    }

    public override (int KeyFrameIndex, float FramePlayingTime) AdvancePlayback(object context, int prevKeyFrameIndex, float prevPlayingTime, float frameTime)
    {
        var durationSeconds = (float)Duration.TotalSeconds;
        var playingTime = prevPlayingTime + frameTime;
        var normalizedPlayingTime = playingTime / durationSeconds;
        var easedFrameTime = frameTime;
        if (normalizedPlayingTime < 1f)
        {
            normalizedPlayingTime = EasingsExtensions.Ease(Easing, normalizedPlayingTime, CustomEasingFunc);
            var easedPlayingTime = normalizedPlayingTime * durationSeconds;
            easedFrameTime = easedPlayingTime - prevPlayingTime;
        }
        var (frame, _) = Track?.AdvancePlayback(context, prevKeyFrameIndex, prevPlayingTime, easedFrameTime) ?? default;
        return (frame, playingTime);
    }
}