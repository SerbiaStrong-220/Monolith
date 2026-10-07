// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Administration.LogExport;

/// <summary>
/// An export cannot continue without losing records or exceeding its memory bounds.
/// </summary>
public sealed class AdminLogExportDataException(string errorKey) : Exception(errorKey)
{
    public string ErrorKey { get; } = errorKey;
}
