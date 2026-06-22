using System.Numerics;
using Content.Server._Exodus.Nebula.Components;
using Content.Server._Exodus.Nebula.Generation;
using Content.Server._Exodus.Nebula.Hazards;
using Content.Server._Exodus.Nebula.Presence;
using Content.Server.Administration;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Hazards;
using Content.Shared.Administration;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Console;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula.Admin;

[AdminCommand(AdminFlags.Debug)]
public sealed class NebulaDebugVisualizeCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entityManager = default!;

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
    [Dependency] private IEntityManager _entityManager = default!;

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
    [Dependency] private IEntityManager _entityManager = default!;

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
    [Dependency] private IEntityManager _entityManager = default!;

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
    [Dependency] private IEntityManager _entityManager = default!;

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

        // Compute the same world position the presence system uses — AABB center for grids,
        // world transform position for free-space entities. Helpful for debugging the P1 fix
        // where capships are tested by hull center, not transform origin.
        Vector2? position = null;
        string? source = null;
        if (_entityManager.TryGetComponent<TransformComponent>(entity, out var xform))
        {
            var presenceSystem = _entityManager.System<NebulaPresenceSystem>();
            position = presenceSystem.GetPresencePosition(entity, xform);
            source = _entityManager.HasComponent<MapGridComponent>(entity)
                ? "grid AABB center"
                : "transform position";
        }

        if (!_entityManager.TryGetComponent<NebulaPresenceComponent>(entity, out var presence))
        {
            var posStr = position is { } p ? $" (checked at ({p.X:0}, {p.Y:0}) via {source})" : "";
            shell.WriteLine($"Outside nebula{posStr}.");
            return;
        }

        // NebulaIndex == -1 marks the world-end death zone (no entry in mapComponent.Nebulas).
        var zoneTag = presence.NebulaIndex < 0
            ? "death-zone sub-zone"
            : $"blob nebula {presence.NebulaIndex + 1}";

        var positionStr = position is { } pp ? $" at ({pp.X:0}, {pp.Y:0}) via {source}" : "";
        shell.WriteLine($"Inside {presence.Marker} ({zoneTag}){positionStr}: density {presence.Density:0.00}; alpha {presence.Alpha:0.00}.");
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class NebulaThrustStatusCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entityManager = default!;

    private static readonly (string Name, int Index)[] Directions =
    {
        ("North", 2),
        ("East", 1),
        ("South", 0),
        ("West", 3),
    };

    public string Command => "nebula_thrust_status";
    public string Description => "Prints current shuttle thrust in all four directions for the grid you are standing on.";
    public string Help => "Usage: nebula_thrust_status [details]";

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

        if (shell.Player?.AttachedEntity is not { Valid: true } entity)
        {
            shell.WriteError("No attached entity.");
            return;
        }

        if (!_entityManager.TryGetComponent<TransformComponent>(entity, out var xform))
        {
            shell.WriteError("Attached entity has no transform.");
            return;
        }

        if (xform.GridUid is not { Valid: true } gridUid)
        {
            shell.WriteLine("You are not standing on a grid.");
            return;
        }

        if (!_entityManager.TryGetComponent<ShuttleComponent>(gridUid, out var shuttle))
        {
            shell.WriteLine($"Grid {gridUid} has no ShuttleComponent.");
            return;
        }

        var thrustSystem = _entityManager.System<NebulaShuttleThrustSystem>();
        var inNebula = thrustSystem.TryGetCurrentThrustMultiplier(gridUid, out var multiplier);
        var nebula = GetNebulaLabel(gridUid);
        var lines = new List<string>
        {
            $"Grid {gridUid} shuttle thrust:",
            $"Nebula: {nebula}; slowdown multiplier {multiplier:0.###}.",
        };

        foreach (var direction in Directions)
        {
            AddDirection(lines, gridUid, shuttle, thrustSystem, direction.Name, direction.Index, multiplier, inNebula, details);
        }

        lines.Add($"Angular: thrust {shuttle.AngularThrust:0.##}; thrusters {shuttle.AngularThrusters.Count}.");
        shell.WriteLine(string.Join('\n', lines));
    }

    private void AddDirection(
        List<string> lines,
        EntityUid gridUid,
        ShuttleComponent shuttle,
        NebulaShuttleThrustSystem thrustSystem,
        string name,
        int index,
        float multiplier,
        bool inNebula,
        bool details)
    {
        var raw = shuttle.LinearThrust[index];
        var baseRaw = shuttle.BaseLinearThrust[index];
        var effective = thrustSystem.GetEffectiveDirectionThrust(gridUid, index, raw, multiplier, inNebula);
        var reduction = raw - effective;
        var thrusters = shuttle.LinearThrusters[index];

        lines.Add($"  {name}: raw {raw:0.##}; base {baseRaw:0.##}; effective {effective:0.##}; reduction {reduction:0.##}; thrusters {thrusters.Count}.");

        if (!details)
            return;

        for (var i = 0; i < thrusters.Count; i++)
        {
            var thrusterUid = thrusters[i];
            if (!_entityManager.TryGetComponent<ThrusterComponent>(thrusterUid, out var thruster))
            {
                lines.Add($"    - {GetEntityLabel(thrusterUid)}: missing ThrusterComponent.");
                continue;
            }

            var resistance = thrustSystem.GetThrustReductionResistance(thrusterUid);
            var nebulaThrustMultiplier = thrustSystem.GetNebulaThrustMultiplier(thrusterUid);
            var effectiveThruster = thrustSystem.GetEffectiveThrusterThrust(thrusterUid, thruster.Thrust, multiplier, inNebula);
            lines.Add($"    - {GetEntityLabel(thrusterUid)}: thrust {thruster.Thrust:0.##}; effective {effectiveThruster:0.##}; base {thruster.BaseThrust:0.##}; nebula thrust multiplier {nebulaThrustMultiplier:0.###}; resistance {resistance:0.###}; on {thruster.IsOn}; enabled {thruster.Enabled}.");
        }
    }

    private string GetNebulaLabel(EntityUid gridUid)
    {
        return _entityManager.TryGetComponent<NebulaPresenceComponent>(gridUid, out var presence)
            ? $"{presence.Marker} density {presence.Density:0.##}"
            : "none";
    }

    private string GetEntityLabel(EntityUid uid)
    {
        return _entityManager.TryGetComponent<MetaDataComponent>(uid, out var meta)
            ? $"{meta.EntityName} ({uid})"
            : uid.ToString();
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class NebulaNpcPresenceStatusCommand : IConsoleCommand
{
    private const float MaxNearbyRadius = 5000f;
    private const int MaxNearbyEntries = 100;

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<Entity<NebulaNpcGridPresenceSourceComponent>> _nearbyNpcCoreBuffer = new();
    private readonly HashSet<EntityUid> _nearbyEligibleGrids = new();

    public string Command => "nebula_npc_presence_status";
    public string Description => "Prints NPC AI grid nebula presence lease status.";
    public string Help => "Usage: nebula_npc_presence_status [nearby|grid] [radius=1500]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteError(Help);
            return;
        }

        var mode = args.Length == 0 ? "nearby" : args[0].ToLowerInvariant();
        switch (mode)
        {
            case "grid":
                if (args.Length > 1)
                {
                    shell.WriteError(Help);
                    return;
                }

                PrintGridStatus(shell);
                return;
            case "nearby":
                PrintNearbyStatus(shell, args);
                return;
            default:
                shell.WriteError(Help);
                return;
        }
    }

    private void PrintGridStatus(IConsoleShell shell)
    {
        if (!TryGetAttachedGrid(shell, out var gridUid))
            return;

        var lines = new List<string>
        {
            $"NPC nebula presence for grid {GetEntityLabel(gridUid)}:",
            $"  {FormatPresence(gridUid)}.",
            $"  {FormatLease(gridUid)}.",
            $"  Config: scan radius {NebulaPresenceSystem.NpcGridScanRadius:0.#}; scan interval {FormatDuration(NebulaPresenceSystem.NpcGridScanInterval)}; lease duration {FormatDuration(NebulaPresenceSystem.NpcGridLeaseDuration)}.",
        };

        if (_entityManager.TryGetComponent<NebulaNpcPresenceLeaseComponent>(gridUid, out var lease) &&
            lease.SourceCore.Valid)
        {
            AddSourceCoreStatus(lines, lease.SourceCore, gridUid);
        }

        shell.WriteLine(string.Join('\n', lines));
    }

    private void PrintNearbyStatus(IConsoleShell shell, string[] args)
    {
        if (!TryGetAttachedCoordinates(shell, out var entity, out var origin))
            return;

        var radius = NebulaPresenceSystem.NpcGridScanRadius;
        if (args.Length == 2 &&
            (!float.TryParse(args[1], out radius) || radius <= 0f || radius > MaxNearbyRadius))
        {
            shell.WriteError($"Radius must be a number from 1 to {MaxNearbyRadius:0}.");
            return;
        }

        _nearbyNpcCoreBuffer.Clear();
        _nearbyEligibleGrids.Clear();
        var lookup = _entityManager.System<EntityLookupSystem>();
        lookup.GetEntitiesInRange(
            origin,
            radius,
            _nearbyNpcCoreBuffer,
            LookupFlags.Uncontained);

        var entries = new List<string>();
        var eligibleCores = 0;
        var skippedCores = 0;
        var printed = 0;

        foreach (var core in _nearbyNpcCoreBuffer)
        {
            var ok = TryGetCoreGridStatus(core.Owner, origin.MapId, out var gridUid, out var reason);
            if (ok)
            {
                eligibleCores++;
                _nearbyEligibleGrids.Add(gridUid);
            }
            else
            {
                skippedCores++;
            }

            if (printed >= MaxNearbyEntries)
                continue;

            entries.Add(ok
                ? $"  - {GetEntityLabel(core.Owner)}: OK; grid {GetEntityLabel(gridUid)}; {FormatPresence(gridUid)}; {FormatLease(gridUid)}."
                : $"  - {GetEntityLabel(core.Owner)}: skipped ({reason}).");
            printed++;
        }

        var lines = new List<string>
        {
            $"NPC nebula cores near {GetEntityLabel(entity)} within {radius:0.#}:",
            $"  Found cores: {_nearbyNpcCoreBuffer.Count}; eligible cores: {eligibleCores}; unique eligible grids: {_nearbyEligibleGrids.Count}; skipped: {skippedCores}.",
            $"  Config: automatic scan radius {NebulaPresenceSystem.NpcGridScanRadius:0.#}; scan interval {FormatDuration(NebulaPresenceSystem.NpcGridScanInterval)}; lease duration {FormatDuration(NebulaPresenceSystem.NpcGridLeaseDuration)}.",
            $"  Current player auto-scan source: {FormatCurrentPlayerScanEligibility(shell, origin.MapId)}.",
        };

        if (entries.Count == 0)
            lines.Add("  No marked NPC AI cores found.");
        else
            lines.AddRange(entries);

        if (_nearbyNpcCoreBuffer.Count > MaxNearbyEntries)
            lines.Add($"  Output truncated at {MaxNearbyEntries} entries.");

        shell.WriteLine(string.Join('\n', lines));
    }

    private string FormatCurrentPlayerScanEligibility(IConsoleShell shell, MapId mapId)
    {
        if (shell.Player is not { } session)
            return "no session";

        if (session.Status != SessionStatus.InGame)
            return $"not eligible ({session.Status})";

        if (session.AttachedEntity is not { Valid: true } player)
            return "not eligible (no attached entity)";

        if (_entityManager.Deleted(player))
            return "not eligible (deleted entity)";

        if (_entityManager.HasComponent<GhostComponent>(player))
            return "not eligible (ghost)";

        if (_entityManager.System<MobStateSystem>().IsDead(player))
            return "not eligible (dead)";

        if (!_entityManager.System<SharedMindSystem>().TryGetMind(player, out _, out _))
            return "not eligible (no mind)";

        if (!_entityManager.TryGetComponent<TransformComponent>(player, out var xform))
            return "not eligible (missing transform)";

        if (xform.MapID != mapId)
            return $"not eligible (different map {xform.MapID})";

        return "eligible";
    }

    private bool TryGetAttachedGrid(IConsoleShell shell, out EntityUid gridUid)
    {
        gridUid = default;

        if (shell.Player?.AttachedEntity is not { Valid: true } entity)
        {
            shell.WriteError("No attached entity.");
            return false;
        }

        if (!_entityManager.TryGetComponent<TransformComponent>(entity, out var xform))
        {
            shell.WriteError("Attached entity has no transform.");
            return false;
        }

        if (xform.GridUid is not { Valid: true } grid)
        {
            shell.WriteLine("You are not standing on a grid.");
            return false;
        }

        gridUid = grid;
        return true;
    }

    private bool TryGetAttachedCoordinates(IConsoleShell shell, out EntityUid entity, out MapCoordinates coordinates)
    {
        entity = default;
        coordinates = default;

        if (shell.Player?.AttachedEntity is not { Valid: true } attached)
        {
            shell.WriteError("No attached entity.");
            return false;
        }

        if (!_entityManager.TryGetComponent<TransformComponent>(attached, out var xform))
        {
            shell.WriteError("Attached entity has no transform.");
            return false;
        }

        entity = attached;
        coordinates = _entityManager.System<SharedTransformSystem>().GetMapCoordinates(attached, xform);
        return true;
    }

    private bool TryGetCoreGridStatus(EntityUid coreUid, MapId mapId, out EntityUid gridUid, out string reason)
    {
        gridUid = default;
        reason = "ok";

        if (_entityManager.Deleted(coreUid))
        {
            reason = "deleted";
            return false;
        }

        if (!_entityManager.TryGetComponent<TransformComponent>(coreUid, out var xform))
        {
            reason = "missing TransformComponent";
            return false;
        }

        if (xform.MapID != mapId)
        {
            reason = $"different map {xform.MapID}";
            return false;
        }

        if (!xform.Anchored)
        {
            reason = "unanchored";
            return false;
        }

        if (xform.GridUid is not { Valid: true } grid)
        {
            reason = "no grid";
            return false;
        }

        if (_entityManager.Deleted(grid))
        {
            reason = "grid deleted";
            return false;
        }

        if (!_entityManager.HasComponent<MapGridComponent>(grid))
        {
            reason = "parent entity is not a grid";
            return false;
        }

        if (_entityManager.TryGetComponent<ApcPowerReceiverComponent>(coreUid, out var receiver) &&
            !_entityManager.System<PowerReceiverSystem>().IsPowered(coreUid, receiver))
        {
            reason = "unpowered";
            return false;
        }

        gridUid = grid;
        return true;
    }

    private void AddSourceCoreStatus(List<string> lines, EntityUid coreUid, EntityUid expectedGrid)
    {
        var mapId = _entityManager.TryGetComponent<TransformComponent>(expectedGrid, out var gridXform)
            ? gridXform.MapID
            : MapId.Nullspace;
        var ok = TryGetCoreGridStatus(coreUid, mapId, out var actualGrid, out var reason);
        var gridMatch = ok && actualGrid == expectedGrid ? "grid ok" : $"grid mismatch: {actualGrid}";
        lines.Add(ok
            ? $"  Source core: {GetEntityLabel(coreUid)}; active; {gridMatch}."
            : $"  Source core: {GetEntityLabel(coreUid)}; inactive ({reason}).");
    }

    private string FormatPresence(EntityUid gridUid)
    {
        if (!_entityManager.TryGetComponent<NebulaPresenceComponent>(gridUid, out var presence))
            return "presence none";

        var zone = presence.NebulaIndex < 0
            ? "death-zone sub-zone"
            : $"blob nebula {presence.NebulaIndex + 1}";
        return $"presence {presence.Marker} ({zone}), density {presence.Density:0.##}, alpha {presence.Alpha:0.##}";
    }

    private string FormatLease(EntityUid gridUid)
    {
        if (!_entityManager.TryGetComponent<NebulaNpcPresenceLeaseComponent>(gridUid, out var lease))
            return "lease none";

        var curTime = _timing.CurTime;
        var active = lease.ExpiresAt > curTime;
        var expiry = active
            ? $"expires in {FormatDuration(lease.ExpiresAt - curTime)}"
            : $"expired {FormatDuration(curTime - lease.ExpiresAt)} ago";
        var refresh = lease.LastRefresh == TimeSpan.Zero
            ? "last refresh n/a"
            : $"last refresh {FormatDuration(curTime - lease.LastRefresh)} ago";
        var source = lease.SourceCore.Valid ? GetEntityLabel(lease.SourceCore) : "none";
        return $"lease {(active ? "active" : "expired")}, {expiry}, {refresh}, source {source}";
    }

    private string GetEntityLabel(EntityUid uid)
    {
        return _entityManager.TryGetComponent<MetaDataComponent>(uid, out var meta)
            ? $"{meta.EntityName} ({uid})"
            : uid.ToString();
    }

    private static string FormatDuration(TimeSpan value)
    {
        var sign = value < TimeSpan.Zero ? "-" : "";
        value = value.Duration();
        return $"{sign}{value.TotalSeconds:0.##}s";
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class NebulaHazardStatusCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IGameTiming _timing = default!;

    public string Command => "nebula_hazard_status";
    public string Description => "Prints nebula hazard timers for the grid you are standing on.";
    public string Help => "Usage: nebula_hazard_status";

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

        if (!_entityManager.TryGetComponent<TransformComponent>(entity, out var xform))
        {
            shell.WriteError("Attached entity has no transform.");
            return;
        }

        if (xform.GridUid is not { Valid: true } gridUid)
        {
            shell.WriteLine("You are not standing on a grid.");
            return;
        }

        var hasLightning = _entityManager.TryGetComponent<NebulaLightningGridHazardComponent>(gridUid, out var lightning);
        var hasEmp = _entityManager.TryGetComponent<NebulaEmpGridHazardComponent>(gridUid, out var emp);

        if (!hasLightning && !hasEmp)
        {
            shell.WriteLine($"Grid {gridUid} has no active nebula hazards.");
            return;
        }

        var curTime = _timing.CurTime;
        var lines = new List<string> { $"Grid {gridUid} nebula hazards:" };

        if (hasLightning && lightning != null)
        {
            lines.Add($"Lightning ({lightning.Marker}):");
            if (NebulaQueryHelper.TryGetMarkerComponent<NebulaLightningHazardComponent>(
                    _prototype,
                    _componentFactory,
                    lightning.Marker,
                    out var lightningConfig))
            {
                AddLightningTier(lines, "Small", lightningConfig.EnableSmall, lightning.TimersInitialized,
                    lightning.NextSmallStrike, lightning.SmallStrikeCount, lightning.LastSmallStrike,
                    lightning.LastSmallDelta, curTime);
                AddLightningTier(lines, "Heavy", lightningConfig.EnableHeavy, lightning.TimersInitialized,
                    lightning.NextHeavyStrike, lightning.HeavyStrikeCount, lightning.LastHeavyStrike,
                    lightning.LastHeavyDelta, curTime);
                AddLightningTier(lines, "SuperHeavy", lightningConfig.EnableSuperHeavy, lightning.TimersInitialized,
                    lightning.NextSuperHeavyStrike, lightning.SuperHeavyStrikeCount, lightning.LastSuperHeavyStrike,
                    lightning.LastSuperHeavyDelta, curTime);
            }
            else
            {
                lines.Add("  Config unavailable; enable flags unknown.");
                AddLightningTier(lines, "Small", true, lightning.TimersInitialized,
                    lightning.NextSmallStrike, lightning.SmallStrikeCount, lightning.LastSmallStrike,
                    lightning.LastSmallDelta, curTime);
                AddLightningTier(lines, "Heavy", true, lightning.TimersInitialized,
                    lightning.NextHeavyStrike, lightning.HeavyStrikeCount, lightning.LastHeavyStrike,
                    lightning.LastHeavyDelta, curTime);
                AddLightningTier(lines, "SuperHeavy", true, lightning.TimersInitialized,
                    lightning.NextSuperHeavyStrike, lightning.SuperHeavyStrikeCount, lightning.LastSuperHeavyStrike,
                    lightning.LastSuperHeavyDelta, curTime);
            }

            lines.Add($"  Tile cache: {lightning.CachedStrikeTiles.Count} tiles, " +
                      $"refresh {FormatNextTimer(true, lightning.StrikeTileCacheInitialized, lightning.NextStrikeTileCacheRefresh, curTime)}.");
        }

        if (hasEmp && emp != null)
        {
            lines.Add($"EMP ({emp.Marker}):");
            var enabled =
                !NebulaQueryHelper.TryGetMarkerComponent<NebulaEmpHazardComponent>(
                    _prototype,
                    _componentFactory,
                    emp.Marker,
                    out var empConfig) ||
                empConfig.Enabled;
            lines.Add($"  Pulse: {FormatNextTimer(enabled, emp.TimersInitialized, emp.NextPulse, curTime)}, " +
                      $"count {emp.PulseCount}, last {FormatOptionalDuration(emp.LastPulse)}, " +
                      $"delta {FormatOptionalDuration(emp.LastPulseDelta)}.");
            lines.Add($"  Tile cache: {emp.CachedPulseTiles.Count} tiles, " +
                      $"refresh {FormatNextTimer(true, emp.PulseTileCacheInitialized, emp.NextPulseTileCacheRefresh, curTime)}.");
        }

        shell.WriteLine(string.Join('\n', lines));
    }

    private static void AddLightningTier(
        List<string> lines,
        string name,
        bool enabled,
        bool timersInitialized,
        TimeSpan nextTime,
        int count,
        TimeSpan lastTime,
        TimeSpan delta,
        TimeSpan curTime)
    {
        lines.Add($"  {name}: {FormatNextTimer(enabled, timersInitialized, nextTime, curTime)}, " +
                  $"count {count}, last {FormatOptionalDuration(lastTime)}, " +
                  $"delta {FormatOptionalDuration(delta)}.");
    }

    private static string FormatNextTimer(bool enabled, bool timersInitialized, TimeSpan nextTime, TimeSpan curTime)
    {
        if (!enabled)
            return "disabled";

        if (!timersInitialized || nextTime == TimeSpan.Zero)
            return "pending";

        return $"next in {FormatDuration(nextTime - curTime)}";
    }

    private static string FormatOptionalDuration(TimeSpan value)
    {
        return value == TimeSpan.Zero ? "n/a" : FormatDuration(value);
    }

    private static string FormatDuration(TimeSpan value)
    {
        var sign = value < TimeSpan.Zero ? "-" : "";
        value = value.Duration();
        return $"{sign}{value.TotalSeconds:0.##}s";
    }
}
