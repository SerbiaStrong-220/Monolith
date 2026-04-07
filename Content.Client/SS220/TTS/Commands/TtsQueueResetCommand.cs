// (c) Space Exodus Team - EXDS-RL with CLA

using Robust.Shared.Console;

namespace Content.Client.SS220.TTS.Commands;

public sealed class TtsQueueResetCommand : IConsoleCommand
{
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;

    public string Command => "ttsqueuereset";
    public string Description => "Reset local TTS queue";
    public string Help => "ttsqueuereset";
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var ttsSys = _entitySystemManager.GetEntitySystem<TTSSystem>();
        ttsSys.ResetQueuesAndEndStreams();

        shell.WriteLine("Local TTS queue has been reset.");
    }
}
