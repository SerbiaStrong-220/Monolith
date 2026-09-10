using Content.Server._Exodus.War; // Exodus: pairwise faction wars.
using Content.Shared._Exodus.Territory; // Exodus: pairwise faction wars.
using Robust.Shared.Prototypes; // Exodus: pairwise faction wars.

namespace Content.Server._Mono.AlertLevel;

public sealed partial class WarLevelSystem : EntitySystem
{
    // Exodus-begin: retain the legacy API while delegating state changes to the pairwise war system.
    private static readonly ProtoId<TerritoryFactionPrototype> LegacyDeclaringFaction = "TSFMC";
    private static readonly ProtoId<TerritoryFactionPrototype> LegacyTargetFaction = "PDV";

    [Dependency] private FactionWarSystem _factionWar = default!;

    public bool GetWarLevel(EntityUid station, WarLevelComponent? alert = null)
    {
        return _factionWar.TryGetState(out var state) && state.Comp.PostWar;
    }

    public void SetLevel(bool level, WarLevelComponent? component = null)
    {
        if (level)
        {
            var result = _factionWar.TryDeclareWar(
                LegacyDeclaringFaction,
                LegacyTargetFaction,
                force: true);

            if (result != WarDeclarationResult.Success && result != WarDeclarationResult.AlreadyAtWar)
                Log.Error($"Failed to enable the legacy war level: {result}.");

            return;
        }

        // Legacy COLD means that no faction pair is at war, so this intentionally clears every declaration.
        if (!_factionWar.TryGetState(out _))
        {
            Log.Error("Failed to disable the legacy war level: the sector war state is unavailable.");
            return;
        }

        _factionWar.ClearAllWars();
    }
    // Exodus-end
}

public sealed class WarLevelChangedEvent : EntityEventArgs
{
    public bool WarLevel { get; }

    public WarLevelChangedEvent(bool alertLevel)
    {
        WarLevel = alertLevel;
    }
}
