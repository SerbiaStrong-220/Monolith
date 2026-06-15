using System.Numerics;
using Content.Client._Exodus.Nebula;
using Content.Shared._Exodus.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    private readonly NebulaSystem _nebula;

    private bool CanFTLToNebulaPreview(EntityUid shuttleUid, EntityCoordinates targetCoordinates, Angle targetAngle)
    {
        return _nebula.CanFTL(shuttleUid, targetCoordinates, targetAngle, out _);
    }

    // Exodus-begin faction AI FTL map label
    private string AddFactionAiControlLabel(EntityUid grid, string labelText)
    {
        if (!EntManager.TryGetComponent(grid, out FactionAiControlledGridComponent? control) ||
            !TryGetFactionAiControlLabel(control, out var controlLabel))
        {
            return labelText;
        }

        return $"{labelText}\n{controlLabel}";
    }

    private bool TryGetFactionAiControlLabel(FactionAiControlledGridComponent control, out string label)
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
        if (PrototypeManager.TryIndex(factionId, out NpcFactionPrototype? faction))
        {
            if (faction.CoreControlName is { } coreControlName)
                factionName = Loc.GetString(coreControlName);
            else if (faction.Name is { } name)
                factionName = Loc.GetString(name);
        }

        label = Loc.GetString("radar-console-core-control-label", ("faction", factionName.ToUpperInvariant()));
        return true;
    }

    private void DrawMapObjectLabel(DrawingHandleScreen handle, Vector2 position, string text, Color color)
    {
        var lines = text.Split('\n');
        var y = 0f;

        foreach (var line in lines)
        {
            var dimensions = handle.GetDimensions(_font, line, 1f);
            var offset = new Vector2(-dimensions.X / 2f, y + dimensions.Y * UIScale);
            handle.DrawString(_font, position + offset, line, color);
            y += dimensions.Y * UIScale;
        }
    }
    // Exodus-end
}
