using System.Linq;
using Content.Server._Mono.Projectiles.TargetGuided;
using Content.Shared._Exodus.FireControl; // Exodus fire-control cursor optimization
using Content.Shared._Mono.FireControl;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Shuttles.Components;
using EntityCoordinates = Robust.Shared.Map.EntityCoordinates;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    [Dependency] private TargetGuidedSystem _targetGuided = null!;

    /// <summary>
    /// List of active guided missiles that need cursor position updates
    /// </summary>
    private readonly HashSet<EntityUid> _activeMissiles = new();

    /// <summary>
    /// Map of console entities to their current mouse positions
    /// </summary>
    private readonly Dictionary<EntityUid, EntityCoordinates> _consoleMousePositions = new();

    // Exodus-begin fire-control cursor optimization
    private readonly HashSet<EntityUid> _consoleFiringWeapons = new();
    private readonly List<EntityUid> _finishedConsoleFiringWeapons = new();
    // Exodus-end

    /// <summary>
    /// Registers handlers for events related to target guided projectiles.
    /// </summary>
    private void InitializeTargetGuided()
    {
        SubscribeLocalEvent<GunComponent, AmmoShotEvent>(OnTargetGuidedShot);
        SubscribeLocalEvent<TargetGuidedComponent, ComponentShutdown>(OnGuidedMissileShutdown);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleCursorPositionEvent>(OnConsoleCursorPosition); // Exodus fire-control cursor optimization
    }

    /// <summary>
    /// Track console cursor events to update guided projectile targets.
    /// </summary>
    private void OnConsoleCursorPosition(Entity<FireControlConsoleComponent> ent, ref FireControlConsoleCursorPositionEvent args) // Exodus fire-control cursor optimization
    {
        if (!_consoleMousePositions.ContainsKey(ent))
            return;

        _consoleMousePositions[ent] = args.Coordinates;
    }

    /// <summary>
    /// Subscribed to AmmoShotEvent to check for and configure guided projectiles.
    /// </summary>
    private void OnTargetGuidedShot(EntityUid uid, GunComponent component, AmmoShotEvent args)
    {
        if (args.FiredProjectiles.Count == 0)
            return;

        // Get the shooter entity
        EntityUid? shooter = null;
        if (TryComp<ProjectileComponent>(args.FiredProjectiles[0], out var projectileComp))
        {
            shooter = projectileComp.Shooter;
        }

        // We need to get the target coordinates from the gun component
        var targetCoords = component.ShootCoordinates;
        if (!targetCoords.HasValue || !targetCoords.Value.IsValid(EntityManager))
            return;

        // Find the controlling console for position updates if this is a fire controllable
        EntityUid? controllingConsole = null;
        if (TryComp<FireControllableComponent>(uid, out var fireControllable) &&
            fireControllable.ControllingServer is { } controllingServer)
        {
            // Exodus-begin fire-control cursor optimization
            if (fireControllable.ActiveFiringConsole is { } firingConsole
                && fireControllable.ActiveFiringUser == shooter
                && TryComp<FireControlConsoleComponent>(firingConsole, out var firingConsoleComponent)
                && firingConsoleComponent.ConnectedServer == controllingServer
                && TryComp<FireControlServerComponent>(controllingServer, out var server)
                && server.Consoles.Contains(firingConsole))
            {
                controllingConsole = firingConsole;

                if (!_consoleMousePositions.ContainsKey(firingConsole))
                    _consoleMousePositions[firingConsole] = targetCoords.Value;
            }
            else if (fireControllable.ActiveFiringConsole != null)
            {
                // A shot from another source must not inherit an earlier console's cursor.
                ClearConsoleFireSource(uid, fireControllable);
            }
            // Exodus-end
        }

        foreach (var projectileUid in args.FiredProjectiles)
        {
            if (!TryComp<TargetGuidedComponent>(projectileUid, out var guidedComp))
                continue;

            // If firing ship is in FTL, missile won't have guidance
            if (shooter.HasValue && Transform(shooter.Value).GridUid is { } shipGrid)
            {
                if (TryComp<FTLComponent>(shipGrid, out _))
                {
                    // Skip guidance setup if ship is in FTL
                    continue;
                }
            }

            // Set up initial target for guided missile
            guidedComp.TargetPosition = targetCoords.Value;

            // Add to our tracking list for cursor position updates
            _activeMissiles.Add(projectileUid);

            // Record the console this was fired from for position updates
            if (controllingConsole.HasValue)
            {
                guidedComp.ControllingConsole = controllingConsole;
            }
        }
    }

    /// <summary>
    /// Cleanup guided missiles when they're destroyed
    /// </summary>
    private void OnGuidedMissileShutdown(EntityUid uid, TargetGuidedComponent component, ComponentShutdown args)
    {
        _activeMissiles.Remove(uid);
    }

    /// <summary>
    /// Updates the cursor position for any tracking missiles from a given console
    /// </summary>
    public void OnGuidanceUpdate(EntityUid consoleUid, EntityCoordinates targetCoordinates)
    {
        // Store the updated position for this console
        _consoleMousePositions[consoleUid] = targetCoordinates;

        // Update any active missiles being controlled by this console
        foreach (var missile in _activeMissiles)
        {
            if (!TryComp<TargetGuidedComponent>(missile, out var guidedComp))
                continue;

            if (guidedComp.ControllingConsole != consoleUid)
                continue;

            // Don't update position if the missile's ship is in FTL
            if (TryComp<ProjectileComponent>(missile, out var projectileComp) &&
                projectileComp.Shooter.HasValue &&
                Transform(projectileComp.Shooter.Value).GridUid is { } shipGrid &&
                TryComp<FTLComponent>(shipGrid, out _))
            {
                continue;
            }

            guidedComp.TargetPosition = targetCoordinates;
        }
    }

    /// <summary>
    /// Helper method to get the current position of a specific console
    /// </summary>
    public EntityCoordinates? GetConsolePosition(EntityUid consoleUid)
    {
        if (_consoleMousePositions.TryGetValue(consoleUid, out var coords))
            return coords;

        return null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Update target positions for active missiles based on the current cursor position
        foreach (var missileUid in _activeMissiles.ToArray())
        {
            if (!TryComp<TargetGuidedComponent>(missileUid, out var guidedComp) ||
                !guidedComp.ControllingConsole.HasValue)
                continue;

            // Get the controlling console
            var consoleUid = guidedComp.ControllingConsole.Value;
            if (!_consoleMousePositions.TryGetValue(consoleUid, out var mousePosition))
                continue;

            // Don't update position if the missile's ship is in FTL
            if (TryComp<ProjectileComponent>(missileUid, out var projectileComp) &&
                projectileComp.Shooter.HasValue &&
                Transform(projectileComp.Shooter.Value).GridUid is { } shipGrid &&
                TryComp<FTLComponent>(shipGrid, out _))
            {
                continue;
            }

            // Update the missile's target to the console's current mouse position
            _targetGuided.SetTargetPosition(missileUid, mousePosition);
        }

        // Clean up any console positions for consoles that no longer exist or have no active missiles
        CleanupConsolePositions();
        CleanupConsoleFireSources(); // Exodus fire-control cursor optimization
        ProcessPendingUiUpdates(); // Exodus fire-control event-driven UI updates
    }

    // Exodus-begin fire-control cursor optimization
    private void CleanupConsoleFireSources()
    {
        if (_consoleFiringWeapons.Count == 0)
            return;

        _finishedConsoleFiringWeapons.Clear();
        foreach (var weapon in _consoleFiringWeapons)
        {
            if (!TryComp<FireControllableComponent>(weapon, out var controllable))
            {
                _finishedConsoleFiringWeapons.Add(weapon);
                continue;
            }

            if (!_gunQuery.TryComp(weapon, out var gun)
                || !_autoShootQuery.TryComp(weapon, out var autoShoot)
                || autoShoot.RemainingTime <= TimeSpan.Zero && !gun.BurstActivated)
            {
                ResetConsoleFireSource(controllable);
                _finishedConsoleFiringWeapons.Add(weapon);
            }
        }

        foreach (var weapon in _finishedConsoleFiringWeapons)
            _consoleFiringWeapons.Remove(weapon);
    }
    // Exodus-end

    /// <summary>
    /// Remove any console positions that no longer have active missiles
    /// </summary>
    private void CleanupConsolePositions()
    {
        // Get all consoles that are actually controlling missiles
        var activeConsoles = new HashSet<EntityUid>();
        foreach (var missileUid in _activeMissiles)
        {
            if (TryComp<TargetGuidedComponent>(missileUid, out var guidedComp) &&
                guidedComp.ControllingConsole.HasValue)
            {
                activeConsoles.Add(guidedComp.ControllingConsole.Value);
            }
        }

        // Remove positions for consoles without any missiles
        foreach (var consoleUid in _consoleMousePositions.Keys.ToList())
        {
            if (!activeConsoles.Contains(consoleUid) || !EntityManager.EntityExists(consoleUid))
            {
                _consoleMousePositions.Remove(consoleUid);
            }
        }
    }
}
