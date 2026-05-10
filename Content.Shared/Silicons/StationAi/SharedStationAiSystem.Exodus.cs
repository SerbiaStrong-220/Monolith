using Content.Shared.IdentityManagement;

namespace Content.Shared.Silicons.StationAi;

/// <summary>
/// Exodus: integrates the AI shell rename feature into upstream identity hooks.
/// Kept in a partial file so upstream sync edits to <see cref="SharedStationAiSystem"/> stay clean.
/// </summary>
public abstract partial class SharedStationAiSystem
{
    /// <summary>
    /// When a Station AI shell has a renamed core, replace the identity short-info title
    /// with the core name so chat and admin tooling display the renamed shell instead of the puppet.
    /// </summary>
    private void OverrideAiIdentityTitle(EntityUid forActor, ref TryGetIdentityShortInfoEvent args)
    {
        if (!TryGetCore(forActor, out var core))
            return;

        args.Title = Name(core.Owner);
    }
}
