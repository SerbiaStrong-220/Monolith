using Content.Shared._Exodus.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.NPC;

/// <summary>
/// Shared formatting for faction AI core-control labels on radar and BSS map UIs.
/// </summary>
public static class FactionAiControlLabelHelper
{
    public static string AppendToLabel(
        string labelText,
        FactionAiControlledGridComponent? control,
        IPrototypeManager prototypes)
    {
        if (control == null || !TryFormat(control, prototypes, out var controlLabel))
            return labelText;

        return $"{labelText}\n{controlLabel}";
    }

    public static bool TryFormat(
        FactionAiControlledGridComponent control,
        IPrototypeManager prototypes,
        out string label)
    {
        if (control.State == FactionAiControlState.Contested)
        {
            label = Loc.GetString("radar-console-core-control-contested-label");
            return true;
        }

        if (control.Faction is not { } factionId)
        {
            label = string.Empty;
            return false;
        }

        var factionName = factionId.Id;
        if (prototypes.TryIndex(factionId, out NpcFactionPrototype? faction))
        {
            if (faction.CoreControlName is { } coreControlName)
                factionName = Loc.GetString(coreControlName);
            else if (faction.Name is { } name)
                factionName = Loc.GetString(name);
        }

        label = Loc.GetString("radar-console-core-control-label", ("faction", factionName.ToUpperInvariant()));
        return true;
    }
}