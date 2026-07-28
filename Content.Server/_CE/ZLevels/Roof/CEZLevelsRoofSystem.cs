/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._CE.ZLevels.Roof;
using Content.Shared._CE.ZLevels.Core.EntitySystems;

namespace Content.Server._CE.ZLevels.Roof;

public sealed partial class CEZLevelsRoofSystem : CESharedZLevelsRoofSystem
{
    private readonly HashSet<Vector2i> _roofMap = new();

    public override void Initialize()
    {
        base.Initialize();

        if (!CESharedZLevelsSystem.ZLevelsEnabled) // Exodus-disable-z-levels
            return;

        InitMaps();
        InitGrids();
    }
}
