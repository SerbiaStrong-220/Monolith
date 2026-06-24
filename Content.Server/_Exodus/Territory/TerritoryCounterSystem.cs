using Content.Shared._Exodus.Territory;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Tracks captured territory score per faction.
/// Recalculates on round start, updates by delta on claim changes, and keeps every declared territory faction present at score 0.
/// </summary>
public sealed partial class TerritoryCounterSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    private readonly Dictionary<ProtoId<TerritoryFactionPrototype>, int> _scores = new();
    private bool _roundStarted;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridTerritoryComponent, GridTerritoryControllerChangedEvent>(OnTerritoryChanged);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        _roundStarted = true;
        RecalculateAll();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _roundStarted = false;
        _scores.Clear();
    }

    private void OnTerritoryChanged(Entity<GridTerritoryComponent> ent, ref GridTerritoryControllerChangedEvent args)
    {
        int points = GetPoints(ent.Comp.Radius);

        if (args.OldFaction is { } oldF)
        {
            EnsureFaction(oldF);
            _scores.TryGetValue(oldF, out int oldScore);
            int newScore = Math.Max(0, oldScore - points);
            if (oldScore != newScore)
            {
                _scores[oldF] = newScore;
                if (_roundStarted)
                {
                    var ev = new TerritoryScoreChangedEvent(oldF, oldScore, newScore);
                    RaiseLocalEvent(ref ev);
                }
            }
        }

        if (args.NewFaction is { } newF)
        {
            EnsureFaction(newF);
            _scores.TryGetValue(newF, out int oldScore);
            int newScore = oldScore + points;
            if (oldScore != newScore)
            {
                _scores[newF] = newScore;
                if (_roundStarted)
                {
                    var ev = new TerritoryScoreChangedEvent(newF, oldScore, newScore);
                    RaiseLocalEvent(ref ev);
                }
            }
        }
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<TerritoryFactionPrototype>())
        {
            EnsureAllFactions();
        }
    }

    private void RecalculateAll()
    {
        _scores.Clear();

        var query = EntityQueryEnumerator<GridTerritoryComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.ControllingFaction is not { } f)
                continue;

            int pts = GetPoints(comp.Radius);
            _scores.TryGetValue(f, out int cur);
            _scores[f] = cur + pts;
        }

        EnsureAllFactions();
    }

    private void EnsureAllFactions()
    {
        foreach (var fac in _proto.EnumeratePrototypes<TerritoryFactionPrototype>())
        {
            var key = new ProtoId<TerritoryFactionPrototype>(fac.ID);
            if (!_scores.ContainsKey(key))
                _scores[key] = 0;
        }
    }

    private void EnsureFaction(ProtoId<TerritoryFactionPrototype> faction)
    {
        if (!_scores.ContainsKey(faction))
            _scores[faction] = 0;
    }

    private static int GetPoints(float radius)
    {
        // Equivalent to round-half-up for positive kilometer values.
        if (radius <= 0)
            return 0;

        int r = (int)radius;
        return (r + 500) / 1000;
    }

    /// <summary>
    /// Returns the current captured territory score for the given faction.
    /// Factions come from the territory_factions.yml prototype declarations.
    /// If the faction is not yet known (e.g. very early query or dynamic), returns 0
    /// and ensures it exists for future tracking.
    /// </summary>
    public int GetScore(ProtoId<TerritoryFactionPrototype> faction)
    {
        EnsureFaction(faction);
        _scores.TryGetValue(faction, out var score);
        return score;
    }

    /// <summary>
    /// Returns read-only view of all faction scores (includes every faction from the YML, even at 0).
    /// Automatically reflects any factions added to territory_factions.yml (after round start or proto reload).
    /// Useful for systems that need to iterate or sum across all declared factions.
    /// </summary>
    public IReadOnlyDictionary<ProtoId<TerritoryFactionPrototype>, int> GetAllScores()
    {
        EnsureAllFactions(); // safety for late calls or hot-reloads
        return _scores;
    }
}
