using Content.Server.Chat.Systems;
using Content.Shared._Goobstation.Dash;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Goobstation.Dash;

public sealed partial class DashActionChatSystem : EntitySystem // Exodus: generated dependency injection.
{
    [Dependency] private ChatSystem _chat = default!; // Exodus: generated dependency injection.
    [Dependency] private IPrototypeManager _prototype = default!; // Exodus: generated dependency injection.

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DashActionEvent>(OnDash);
    }

    private void OnDash(DashActionEvent args)
    {
        if (args.Emote == null)
            return;

        if (!_prototype.TryIndex<EmotePrototype>(args.Emote.Value, out var emote))
            return;

        _chat.TryEmoteWithChat(
            args.Performer,
            emote,
            forceEmote: true);
    }
}