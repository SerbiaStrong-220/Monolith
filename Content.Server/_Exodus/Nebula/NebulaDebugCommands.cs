using Content.Server.Administration;
using Content.Shared._Exodus.Nebula;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Exodus.Nebula;

[AdminCommand(AdminFlags.Debug)]
public sealed class NebulaDebugVisualizeCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "nebula_debug_visualize";
    public string Description => "Spawns temporary debug points for generated nebula contours.";
    public string Help => "Usage: nebula_debug_visualize [all|index] [samples=64] [lifetime=180]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 3)
        {
            shell.WriteError(Help);
            return;
        }

        int? nebulaIndex = null;
        if (args.Length >= 1 && !string.Equals(args[0], "all", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(args[0], out var parsedIndex) || parsedIndex <= 0)
            {
                shell.WriteError("Nebula index must be a positive one-based number or 'all'.");
                return;
            }

            nebulaIndex = parsedIndex - 1;
        }

        var samples = 64;
        if (args.Length >= 2 && (!int.TryParse(args[1], out samples) || samples < 8 || samples > 256))
        {
            shell.WriteError("Samples must be an integer from 8 to 256.");
            return;
        }

        var lifetime = 180f;
        if (args.Length >= 3 && (!float.TryParse(args[2], out lifetime) || lifetime < 5f || lifetime > 600f))
        {
            shell.WriteError("Lifetime must be a number from 5 to 600 seconds.");
            return;
        }

        var system = _entityManager.System<NebulaGenerationSystem>();
        if (!system.TrySpawnDebugVisualization(nebulaIndex, samples, lifetime, out var count, out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine($"Spawned {count} nebula debug visual markers for {lifetime:0.#} seconds.");
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class NebulaDebugClearCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "nebula_debug_clear";
    public string Description => "Deletes temporary nebula debug points.";
    public string Help => "Usage: nebula_debug_clear";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var system = _entityManager.System<NebulaGenerationSystem>();
        var count = system.ClearDebugVisuals();
        shell.WriteLine($"Deleted {count} nebula debug visual markers.");
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class NebulaStatusCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "nebula_status";
    public string Description => "Prints generated nebula and marker status.";
    public string Help => "Usage: nebula_status";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var system = _entityManager.System<NebulaGenerationSystem>();
        if (!system.TryGetStatus(out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class NebulaAreaCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "nebula_area";
    public string Description => "Prints total generated nebula area.";
    public string Help => "Usage: nebula_area [details]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }

        var details = args.Length == 1 && string.Equals(args[0], "details", StringComparison.OrdinalIgnoreCase);
        if (args.Length == 1 && !details)
        {
            shell.WriteError(Help);
            return;
        }

        var system = _entityManager.System<NebulaGenerationSystem>();
        if (!system.TryGetAreaStatus(details, out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class NebulaPresenceCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "nebula_presence";
    public string Description => "Prints nebula presence for your attached entity.";
    public string Help => "Usage: nebula_presence";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } entity)
        {
            shell.WriteError("No attached entity.");
            return;
        }

        if (!_entityManager.TryGetComponent<NebulaPresenceComponent>(entity, out var presence))
        {
            shell.WriteLine("Outside nebula.");
            return;
        }

        shell.WriteLine($"Inside {presence.Type} nebula {presence.NebulaIndex + 1}: density {presence.Density:0.00}; alpha {presence.Alpha:0.00}.");
    }
}
