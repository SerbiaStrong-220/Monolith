using Content.Server._Exodus.War; // Exodus: pairwise faction wars.
using Content.Server.Administration;
using Content.Shared._Exodus.Territory; // Exodus: pairwise faction wars.
using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Shared.Console;
using Robust.Shared.Prototypes; // Exodus: pairwise faction wars.

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
            // Exodus-begin: optional directional faction pair.
            return args.Length switch
            {
                1 => CompletionResult.FromHintOptions(CompletionHelper.Booleans,
                    LocalizationManager.GetString("cmd-setwarlevel-hint-1")),
                2 => CompletionResult.FromHintOptions(GetFactionOptions(),
                    LocalizationManager.GetString("cmd-setwarlevel-hint-2")),
                3 => CompletionResult.FromHintOptions(GetFactionOptions(),
                    LocalizationManager.GetString("cmd-setwarlevel-hint-3")),
                _ => CompletionResult.Empty,
            };
            // Exodus-end
        }

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            // Exodus-begin: retain one-argument TSFMC/PDV control and support explicit faction pairs.
            if (args.Length != 1 && args.Length != 3)
            {
                shell.WriteError(LocalizationManager.GetString("shell-wrong-arguments-number"));
                return;
            }

            if (!bool.TryParse(args[0], out var postWar))
            {
                shell.WriteLine(LocalizationManager.GetString("shell-argument-must-be-boolean"));
                return;
            }

            var factionWar = _entitySystems.GetEntitySystem<FactionWarSystem>();
            var actor = shell.Player?.AttachedEntity;

            if (args.Length == 1)
            {
                if (postWar)
                {
                    var result = factionWar.TryDeclareWar(
                        new ProtoId<TerritoryFactionPrototype>("TSFMC"),
                        new ProtoId<TerritoryFactionPrototype>("PDV"),
                        actor,
                        force: true);
                    WriteResult(shell, result, postWar, "TSFMC", "PDV");
                }
                else if (!factionWar.TryGetState(out _))
                {
                    shell.WriteError(LocalizationManager.GetString("cmd-setwarlevel-state-unavailable"));
                }
                else if (!factionWar.ClearAllWars(actor))
                {
                    shell.WriteLine(LocalizationManager.GetString("cmd-setwarlevel-not-active"));
                }

                return;
            }

            var declarer = new ProtoId<TerritoryFactionPrototype>(args[1].Trim());
            var target = new ProtoId<TerritoryFactionPrototype>(args[2].Trim());
            var resultDeclarer = declarer.Id;
            var resultTarget = target.Id;

            if (!postWar &&
                factionWar.TryGetState(out var state) &&
                factionWar.TryGetDeclaration(state, declarer, target, out var existingDeclaration))
            {
                resultDeclarer = existingDeclaration.DeclaringFaction.Id;
                resultTarget = existingDeclaration.TargetFaction.Id;
            }

            var pairResult = postWar
                ? factionWar.TryDeclareWar(declarer, target, actor, force: true)
                : factionWar.TryEndWar(declarer, target, actor, force: true);

            WriteResult(shell, pairResult, postWar, resultDeclarer, resultTarget);
            // Exodus-end
        }

        // Exodus-begin: pairwise faction war command helpers.
        private string[] GetFactionOptions()
        {
            var factionWar = _entitySystems.GetEntitySystem<FactionWarSystem>();
            if (!factionWar.TryGetState(out var state))
                return Array.Empty<string>();

            var options = new string[state.Comp.Factions.Count];
            for (var i = 0; i < state.Comp.Factions.Count; i++)
            {
                options[i] = state.Comp.Factions[i].Id;
            }

            return options;
        }

        private void WriteResult(
            IConsoleShell shell,
            WarDeclarationResult result,
            bool postWar,
            string declarer,
            string target)
        {
            var message = result switch
            {
                WarDeclarationResult.Success => LocalizationManager.GetString(
                    postWar ? "cmd-setwarlevel-success-declared" : "cmd-setwarlevel-success-ended",
                    ("declarer", declarer),
                    ("target", target)),
                WarDeclarationResult.StateUnavailable => LocalizationManager.GetString("cmd-setwarlevel-state-unavailable"),
                WarDeclarationResult.RoundNotRunning => LocalizationManager.GetString("war-declaration-round-not-running"),
                WarDeclarationResult.TooEarly => LocalizationManager.GetString("war-declaration-too-early",
                    ("time", "02:00:00")),
                WarDeclarationResult.InvalidFaction => LocalizationManager.GetString("cmd-setwarlevel-invalid-faction",
                    ("faction", $"{declarer}/{target}")),
                WarDeclarationResult.SameFaction => LocalizationManager.GetString("cmd-setwarlevel-same-faction"),
                WarDeclarationResult.AlreadyAtWar => LocalizationManager.GetString("cmd-setwarlevel-already-active"),
                WarDeclarationResult.NotAtWar => LocalizationManager.GetString("cmd-setwarlevel-not-active"),
                _ => LocalizationManager.GetString("cmd-setwarlevel-failed"),
            };

            if (result == WarDeclarationResult.Success)
                shell.WriteLine(message);
            else
                shell.WriteError(message);
        }
        // Exodus-end
    }
}
