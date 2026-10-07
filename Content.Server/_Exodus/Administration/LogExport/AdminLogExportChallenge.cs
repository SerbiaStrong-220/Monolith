using Content.Shared._Exodus.Administration.LogExport;

namespace Content.Server._Exodus.Administration.LogExport;

/// <summary>A one-use confirmation chain owned by one EUI and its authenticated player.</summary>
public sealed class AdminLogExportChallenge
{
    public Guid Id { get; } = Guid.NewGuid();
    public int RoundId { get; }
    public int Stage { get; private set; } = 1;
    public string Token { get; private set; } = Guid.NewGuid().ToString("N");
    private readonly TimeSpan _expires;

    public AdminLogExportChallenge(int roundId, TimeSpan now)
    {
        RoundId = roundId;
        _expires = now + AdminLogExportLimits.ConfirmationLifetime;
    }

    public bool TryConfirm(Guid id, int stage, string token, TimeSpan now, out bool completed)
    {
        completed = false;
        if (Stage == 0 || id != Id || stage != Stage || now >= _expires || token != Token)
        {
            Cancel();
            return false;
        }

        if (Stage == 3)
        {
            completed = true;
            Cancel();
            return true;
        }

        Stage++;
        Token = Guid.NewGuid().ToString("N");
        return true;
    }

    public void Cancel()
    {
        Stage = 0;
        Token = string.Empty;
    }
}
