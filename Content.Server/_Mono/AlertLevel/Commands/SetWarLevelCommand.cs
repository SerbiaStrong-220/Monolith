using Content.Server.Administration;
using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Shared.Console;

namespace Content.Server._Mono.AlertLevel.Commands
{
    // Exodus: restore the upstream manual war-level control without enabling automatic portstrikes.
    [UsedImplicitly]
    [AdminCommand(AdminFlags.Fun)]
    public sealed partial class SetWarLevelCommand : LocalizedCommands
    {
        [Dependency] private IEntitySystemManager _entitySystems = default!;

        public override string Command => "setwarlevel";

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            return args.Length switch
            {
                1 => CompletionResult.FromHintOptions(CompletionHelper.Booleans,
                    LocalizationManager.GetString("cmd-setwarlevel-hint-1")),
                _ => CompletionResult.Empty,
            };
        }

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length < 1)
            {
                shell.WriteError(LocalizationManager.GetString("shell-wrong-arguments-number"));
                return;
            }

            if (!bool.TryParse(args[0], out var postWar))
            {
                shell.WriteLine(LocalizationManager.GetString("shell-argument-must-be-boolean"));
                return;
            }

            if (shell.Player?.AttachedEntity == null)
            {
                shell.WriteLine(LocalizationManager.GetString("shell-only-players-can-run-this-command"));
                return;
            }

            _entitySystems.GetEntitySystem<WarLevelSystem>().SetLevel(postWar);
        }
    }
}
