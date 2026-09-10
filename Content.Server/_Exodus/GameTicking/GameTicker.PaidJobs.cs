using Content.Server.GameTicking.Events;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Shared._NF.Bank;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    private void InitializePaidJobs()
    {
        SubscribeLocalEvent<GetDisallowedJobsEvent>(OnDisallowAutomaticPaidJobs);
        SubscribeLocalEvent<StationJobsGetCandidatesEvent>(OnPaidJobCandidates);
    }

    private void OnDisallowAutomaticPaidJobs(ref GetDisallowedJobsEvent args)
    {
        var profile = _prefsManager.TryGetCachedPreferences(args.Player.UserId, out var prefs)
            ? prefs.SelectedCharacter as HumanoidCharacterProfile
            : null;

        foreach (var job in _prototypeManager.EnumeratePrototypes<JobPrototype>())
        {
            if (job.EntryPrice > 0 && !CanAutomaticallyAssignPaidJob(args.Player.UserId, profile, job))
                args.Jobs.Add(job.ID);
        }
    }

    private void OnPaidJobCandidates(ref StationJobsGetCandidatesEvent args)
    {
        var profile = _prefsManager.TryGetCachedPreferences(args.Player, out var prefs)
            ? prefs.SelectedCharacter as HumanoidCharacterProfile
            : null;

        for (var i = args.Jobs.Count - 1; i >= 0; i--)
        {
            if (_prototypeManager.TryIndex(args.Jobs[i], out var job) && job.EntryPrice > 0 &&
                !CanAutomaticallyAssignPaidJob(args.Player, profile, job))
            {
                args.Jobs.RemoveAt(i);
            }
        }
    }

    private bool CanAutomaticallyAssignPaidJob(NetUserId player, HumanoidCharacterProfile? profile, JobPrototype job)
    {
        if (profile == null || profile.BankBalance < job.EntryPrice ||
            !profile.JobPriorities.TryGetValue(job.ID, out var priority) || priority != JobPriority.High)
        {
            return false;
        }

        return job.RequiredAntag is not { } antag ||
            _banManager.GetAntagBans(player) is { } bans && !bans.Contains(antag);
    }

    /// <summary>
    /// Prepares paid characters before leaving the lobby. Payment reserves the entry fee before
    /// loadouts can spend the remaining balance, and is refunded if spawning or slot assignment fails.
    /// </summary>
    private bool TrySpawnPaidJob(
        ICommonSession player,
        EntityUid station,
        JobPrototype job,
        bool explicitlySelected,
        ref HumanoidCharacterProfile character,
        out EntityUid? mob)
    {
        mob = null;
        if (job.EntryPrice <= 0)
            return true;

        var highPriority = character.JobPriorities.TryGetValue(job.ID, out var priority) && priority == JobPriority.High;
        var roundStarting = _startingRound && RunLevel == GameRunLevel.PreRoundLobby;
        if ((!explicitlySelected && !highPriority) || (!roundStarting && RunLevel != GameRunLevel.InRound) ||
            !_playerGameStatuses.TryGetValue(player.UserId, out var status) || status == PlayerGameStatus.JoinedGame)
        {
            _chatManager.DispatchServerMessage(player, Loc.GetString("paid-job-selection-required"));
            return false;
        }

        if (job.RequiredAntag is { } antag &&
            (_banManager.GetAntagBans(player.UserId) is not { } bans || bans.Contains(antag)))
        {
            _chatManager.DispatchServerMessage(player, Loc.GetString("role-ban"));
            return false;
        }

        if (Deleted(station) || !HasComp<StationSpawningComponent>(station) ||
            !TryComp<StationJobsComponent>(station, out var stationJobs) ||
            !_stationJobs.TryGetJobSlot(station, job, out var slots, stationJobs) || slots == 0 ||
            !TryGetPaidJobSpawnPoint(station, job, out var coordinates))
        {
            _chatManager.DispatchServerMessage(player, Loc.GetString("paid-job-unavailable"));
            return false;
        }

        if (!_prefsManager.TryGetCachedPreferences(player.UserId, out var prefs) ||
            !prefs.TryIndexOfCharacter(character, out var characterIndex))
        {
            _chatManager.DispatchServerMessage(player, Loc.GetString("paid-job-unavailable"));
            return false;
        }

        if (!_bank.TryBankWithdraw(player, prefs, character, job.EntryPrice, out _))
        {
            _chatManager.DispatchServerMessage(player, Loc.GetString("paid-job-insufficient-funds"));
            return false;
        }

        var completed = false;
        try
        {
            // BankSystem replaces the cached profile. Loadout purchases must use that new balance.
            character = GetPlayerProfile(player);
            mob = _stationSpawning.SpawnPlayerMob(coordinates, job.ID, character, station, session: player);
            if (Deleted(mob.Value) || !MetaData(mob.Value).EntityInitialized ||
                !_stationJobs.TryAssignJob(station, job, player.UserId, stationJobs))
            {
                _chatManager.DispatchServerMessage(player, Loc.GetString("paid-job-unavailable"));
                return false;
            }

            completed = true;
        }
        catch (Exception exception)
        {
            Log.Error($"Could not spawn paid job {job.ID} for {player.UserId}: {exception}");
            _chatManager.DispatchServerMessage(player, Loc.GetString("paid-job-unavailable"));
            return false;
        }
        finally
        {
            if (!completed)
            {
                if (mob is { } failedMob && !Deleted(failedMob))
                    QueueDel(failedMob);
                mob = null;

                // Use the current profile in the original slot, preserving any other balance changes.
                if (!_prefsManager.TryGetCachedPreferences(player.UserId, out var currentPrefs) ||
                    !currentPrefs.Characters.TryGetValue(characterIndex, out var currentCharacter) ||
                    currentCharacter is not HumanoidCharacterProfile currentProfile ||
                    !_bank.TryBankDeposit(player, currentPrefs, currentProfile, job.EntryPrice, out _))
                {
                    Log.Error($"Could not refund paid job {job.ID}: user {player.UserId}, character slot {characterIndex}, amount {job.EntryPrice}.");
                }
            }
        }

        _adminLogger.Add(LogType.LateJoin, LogImpact.Medium,
            $"Player {player.Name} paid {job.EntryPrice} from character slot {characterIndex} to join as {job.ID} on {ToPrettyString(station)}.");
        _chatManager.DispatchServerMessage(player, Loc.GetString("paid-job-purchased",
            ("price", BankSystemExtensions.ToSpesoString(job.EntryPrice))));
        return true;
    }

    private bool TryGetPaidJobSpawnPoint(EntityUid station, JobPrototype job, out EntityCoordinates coordinates)
    {
        coordinates = default;
        var count = 0;
        var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var point, out var xform))
        {
            if (point.SpawnType != SpawnPointType.Job || point.Job != job.ID ||
                xform.GridUid == null || _stationSystem.GetOwningStation(uid, xform) != station)
            {
                continue;
            }

            if (_robustRandom.Next(++count) == 0)
                coordinates = xform.Coordinates;
        }

        return count > 0;
    }
}
