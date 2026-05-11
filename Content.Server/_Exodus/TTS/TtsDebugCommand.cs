using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.SS220.TTS;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Enums;

namespace Content.Server._Exodus.TTS;

[AdminCommand(AdminFlags.Admin)]
public sealed class TtsDebugCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public string Command => "ttsdebug";
    public string Description => "Prints TTS state for all currently player-controlled entities.";
    public string Help => "ttsdebug";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var sessions = _playerManager.Sessions;
        var count = 0;

        shell.WriteLine("=== TTS Debug: player-controlled entities ===");

        foreach (var session in sessions)
        {
            if (session.Status == SessionStatus.Disconnected)
                continue;

            if (session.AttachedEntity is not { } uid)
            {
                shell.WriteLine($"  [{session.Name}] no attached entity");
                continue;
            }

            var meta = _entManager.GetComponent<MetaDataComponent>(uid);
            var entityName = meta.EntityName;
            var prototype = meta.EntityPrototype?.ID ?? "unknown";

            var hasTts = _entManager.TryGetComponent(uid, out TTSComponent? tts);

            string ttsStatus;
            if (!hasTts)
                ttsStatus = "NO TTSComponent";
            else if (tts!.VoicePrototypeId is null)
                ttsStatus = "TTSComponent present but VoicePrototypeId=NULL [BROKEN]";
            else
                ttsStatus = $"voice={tts.VoicePrototypeId}";

            shell.WriteLine($"  [{session.Name}] {entityName} proto={prototype} uid={uid} | {ttsStatus}");
            count++;
        }

        shell.WriteLine($"=== Total: {count} entities checked ===");
    }
}
