using Content.Shared._Exodus.Territory;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Abstract counter of captured territories (per-faction points).
/// 
/// Designed so that future systems can easily reference it for scoring, events, win conditions, etc.
/// 
/// How it works:
/// - On every round start (RoundStartedEvent), it fully recalculates by:
///   1. Scanning all entities with GridTerritoryComponent that have a ControllingFaction.
///   2. For each such claimed territory, adds points based on its Radius (see GetPoints).
///   3. Then explicitly ensures EVERY faction declared in territory_factions.yml has an entry
///      (0 if it has no claims yet).
/// 
/// - Live updates: subscribes to GridTerritoryControllerChangedEvent (raised by GridTerritorySystem
///   whenever a claim changes via banner or other means). On change it does delta: subtract points
///   from old faction, add to new faction. Unknown factions are auto-added via EnsureFaction.
/// 
/// - Automatic pickup of new factions:
///   Because RecalculateAll() and EnsureAllFactions() ALWAYS call
///   _proto.EnumeratePrototypes&lt;TerritoryFactionPrototype&gt;(), any new faction added to
///   Resources/Prototypes/_Exodus/Territory/territory_factions.yml will be automatically
///   "подсасывается" (included with score 0) on the next round start, or immediately if
///   prototypes are reloaded (PrototypesReloadedEventArgs).
/// 
///   Even mid-round, if a brand-new faction claims something, the delta path will create the entry.
/// 
/// Scoring rules (самое оптимальное целочисленное решение):
/// - Rule: points = (r + 500) / 1000   (integer division)
/// - 1km (1000)   → 1 очко
/// - 1.1km (1100) → 1 очко   (1100 не может быть округлено до 2)
/// - 1.5km (1500) → 2 очка   (округлиться до 2 только если 1500 или более)
/// - 2.5km (2500) → 3 очка
/// - 5km (5000)   → 5 очков
/// 
/// Это эквивалент round half up (к ближайшему, .5+ вверх).
/// Такой принцип распространяется на всё округление значений.
/// Самое оптимальное: быстрее, без float, без погрешностей,
/// идеальный задел на будущее.
/// 
/// Public API for other systems:
/// - GetScore(faction)
/// - GetAllScores()
/// - Subscribe to TerritoryScoreChangedEvent for reactive updates.
/// </summary>
public sealed class TerritoryCounterSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly Dictionary<ProtoId<TerritoryFactionPrototype>, int> _scores = new();
    private bool _roundStarted;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridTerritoryControllerChangedEvent>(OnTerritoryChanged);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        _roundStarted = true;
        RecalculateAll();
    }

    private void OnTerritoryChanged(ref GridTerritoryControllerChangedEvent args)
    {
        if (!TryComp<GridTerritoryComponent>(args.Grid, out var comp))
            return;

        int points = GetPoints(comp.Radius);

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
        // Always pull fresh from the YML via prototypes.
        // This is what makes the counter automatically "подсасываться" new factions
        // when they are added to territory_factions.yml (on round start or proto reload).
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
        // Самое оптимальное и чистое решение (целочисленная арифметика):
        //
        // points = (r + 500) / 1000
        //
        // Это математически эквивалентно "round half up" (округление к ближайшему,
        // .5 и выше — вверх) для положительных значений.
        //
        // Правила (точно как просил):
        // - 1км (1000)   → (1000 + 500) / 1000 = 1
        // - 1.1км (1100) → (1100 + 500) / 1000 = 1   (1100 не может быть округлено до 2)
        // - 1.5км (1500) → (1500 + 500) / 1000 = 2   (округлиться до 2 только если 1500 или более)
        // - 2.5км (2500) → (2500 + 500) / 1000 = 3
        // - 3.5км        → (3500 + 500) / 1000 = 4
        // - 5км (5000)   → (5000 + 500) / 1000 = 5
        //
        // Преимущества:
        // - Нет floating point операций после каста
        // - Нет вызова функции округления
        // - Полная защита от погрешностей float
        // - Быстрее и проще
        // - Самое оптимальное решение для этой задачи.
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
