using Content.Server.Shuttles.Components;
using Content.Shared.Database; // Exodus IFF admin logs
using Content.Shared.CCVar;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Events;
using Robust.Server.GameObjects;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    private void InitializeIFF()
    {
        SubscribeLocalEvent<IFFConsoleComponent, AnchorStateChangedEvent>(OnIFFConsoleAnchor);
        SubscribeLocalEvent<IFFConsoleComponent, IFFShowIFFMessage>(OnIFFShow);
        SubscribeLocalEvent<IFFConsoleComponent, IFFShowVesselMessage>(OnIFFShowVessel);
        SubscribeLocalEvent<IFFConsoleComponent, BoundUIOpenedEvent>(OnIFFConsoleOpen);
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
    }

    private void OnGridSplit(ref GridSplitEvent ev)
    {
        var splitMass = _cfg.GetCVar(CCVars.HideSplitGridsUnder);

        if (splitMass < 0)
            return;

        foreach (var grid in ev.NewGrids)
        {
            if (!_physicsQuery.TryGetComponent(grid, out var physics) ||
                physics.Mass > splitMass)
            {
                continue;
            }

            AddIFFFlag(grid, IFFFlags.HideLabel);
        }
    }

    private void OnIFFConsoleOpen(EntityUid uid, IFFConsoleComponent component, ref BoundUIOpenedEvent args)
    {
        // Make sure UI state is up-to-date when opening the UI
        if (!TryComp(uid, out TransformComponent? xform) || xform.GridUid == null)
        {
            _uiSystem.SetUiState(uid, IFFConsoleUiKey.Key, new IFFConsoleBoundUserInterfaceState()
            {
                AllowedFlags = component.AllowedFlags,
                Flags = IFFFlags.None,
            });
            return;
        }

        if (!TryComp<IFFComponent>(xform.GridUid, out var iff))
        {
            _uiSystem.SetUiState(uid, IFFConsoleUiKey.Key, new IFFConsoleBoundUserInterfaceState()
            {
                AllowedFlags = component.AllowedFlags,
                Flags = IFFFlags.None,
            });
            return;
        }

        _uiSystem.SetUiState(uid, IFFConsoleUiKey.Key, new IFFConsoleBoundUserInterfaceState()
        {
            AllowedFlags = component.AllowedFlags,
            Flags = iff.Flags,
        });
    }

    private void OnIFFShow(EntityUid uid, IFFConsoleComponent component, IFFShowIFFMessage args)
    {
        if (!TryComp(uid, out TransformComponent? xform) || xform.GridUid == null ||
            (component.AllowedFlags & IFFFlags.HideLabel) == 0x0)
        {
            return;
        }

        // Exodus-begin IFF admin logs
        if (!TrySetIFFFlagVisibility(uid, xform.GridUid.Value, args.Actor, IFFFlags.HideLabel, args.Show, "IFF label"))
            return;
        // Exodus-end
    }

    private void OnIFFShowVessel(EntityUid uid, IFFConsoleComponent component, IFFShowVesselMessage args)
    {
        if (!TryComp(uid, out TransformComponent? xform) || xform.GridUid == null ||
            (component.AllowedFlags & IFFFlags.Hide) == 0x0)
        {
            return;
        }

        // Exodus-begin IFF admin logs
        if (!TrySetIFFFlagVisibility(uid, xform.GridUid.Value, args.Actor, IFFFlags.Hide, args.Show, "vessel IFF visibility"))
            return;
        // Exodus-end
    }

    // Exodus-begin IFF admin logs
    private bool TrySetIFFFlagVisibility(EntityUid consoleUid, EntityUid gridUid, EntityUid actorUid, IFFFlags flag, bool show, string label)
    {
        if (show)
        {
            if (!TryComp<IFFComponent>(gridUid, out var iff) ||
                iff.ReadOnly ||
                (iff.Flags & flag) == 0x0)
            {
                return false;
            }

            RemoveIFFFlag(gridUid, flag, iff);
            _logger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(actorUid):player} enabled {label} for {ToPrettyString(gridUid):grid} via {ToPrettyString(consoleUid):console}");
            return true;
        }

        var component = CompOrNull<IFFComponent>(gridUid);
        if (component != null &&
            (component.ReadOnly ||
             (component.Flags & flag) == flag))
        {
            return false;
        }

        AddIFFFlag(gridUid, flag, component);
        _logger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(actorUid):player} disabled {label} for {ToPrettyString(gridUid):grid} via {ToPrettyString(consoleUid):console}");
        return true;
    }
    // Exodus-end

    private void OnIFFConsoleAnchor(EntityUid uid, IFFConsoleComponent component, ref AnchorStateChangedEvent args)
    {
        // If we anchor / re-anchor then make sure flags up to date.
        if (!args.Anchored ||
            !TryComp(uid, out TransformComponent? xform) ||
            !TryComp<IFFComponent>(xform.GridUid, out var iff))
        {
            _uiSystem.SetUiState(uid, IFFConsoleUiKey.Key, new IFFConsoleBoundUserInterfaceState()
            {
                AllowedFlags = component.AllowedFlags,
                Flags = IFFFlags.None,
            });
        }
        else
        {
            _uiSystem.SetUiState(uid, IFFConsoleUiKey.Key, new IFFConsoleBoundUserInterfaceState()
            {
                AllowedFlags = component.AllowedFlags,
                Flags = iff.Flags,
            });
        }
    }

    protected override void UpdateIFFInterfaces(EntityUid gridUid, IFFComponent component)
    {
        base.UpdateIFFInterfaces(gridUid, component);

        var query = AllEntityQuery<IFFConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            _uiSystem.SetUiState(uid, IFFConsoleUiKey.Key, new IFFConsoleBoundUserInterfaceState()
            {
                AllowedFlags = comp.AllowedFlags,
                Flags = component.Flags,
            });
        }
    }
}
