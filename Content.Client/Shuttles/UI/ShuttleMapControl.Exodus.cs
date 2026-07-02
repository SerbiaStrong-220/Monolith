using System.Numerics;
using Content.Client._Exodus.Nebula;
using Content.Client._Exodus.NPC;
using Content.Shared._Exodus.NPC.Components;
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

    private string AddFactionAiControlLabel(EntityUid grid, string labelText)
    {
        EntManager.TryGetComponent(grid, out FactionAiControlledGridComponent? control);
        return FactionAiControlLabelHelper.AppendToLabel(labelText, control, PrototypeManager);
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
}
