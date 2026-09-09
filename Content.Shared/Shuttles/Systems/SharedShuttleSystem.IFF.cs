using Content.Shared._Mono.Company;
using Content.Shared.Shuttles.Components;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Shared.Shuttles.Systems;

public abstract partial class SharedShuttleSystem
{
    /*
     * Handles the label visibility on radar controls. This can be hiding the label or applying other effects.
     */

    protected virtual void UpdateIFFInterfaces(EntityUid gridUid, IFFComponent component) {}

    public Color GetIFFColor(EntityUid gridUid, bool self = false, IFFComponent? component = null)
    {
        if (self)
        {
            return IFFComponent.SelfColor;
        }

        // Exodus-begin faction colors are independent of corporate affiliation.
        if (_iffAffiliation.TryGetColor(gridUid, out var affiliationColor))
            return affiliationColor;
        // Exodus-end

        if (!Resolve(gridUid, ref component, false))
        {
            return IFFComponent.IFFColor;
        }

        return component.Color;
    }

    public string? GetIFFLabel(EntityUid gridUid, bool self = false, IFFComponent? component = null)
    {
        var entName = _iffAffiliation.GetGridName(gridUid, MetaData(gridUid).EntityName); // Exodus - transient territory status.

        if (self)
        {
            return entName;
        }

        if (Resolve(gridUid, ref component, false) && (component.Flags & (IFFFlags.HideLabel | IFFFlags.Hide)) != 0x0)
        {
            return null;
        }

        // Exodus-begin configured POIs and military grids use explicit affiliation sources.
        if (_iffAffiliation.TryGetLabel(gridUid, out var affiliation))
        {
            var name = string.IsNullOrEmpty(entName) ? Loc.GetString("shuttle-console-unknown") : entName;
            if (string.IsNullOrEmpty(affiliation))
                return name;

            return Loc.GetString("exodus-iff-affiliation-label", ("name", name), ("affiliation", affiliation));
        }
        // Exodus-end

        // Get the company information if available
        Color? companyColor = Color.White;
        string? companyName = "shuttle-console-company-unknown"; // Exodus resolve the fallback key once, not its translated text.

        if (TryComp<_Mono.Company.CompanyComponent>(gridUid, out var companyComp) && !string.IsNullOrEmpty(companyComp.CompanyName))
        {
            if (IoCManager.Resolve<IPrototypeManager>().TryIndex<CompanyPrototype>(companyComp.CompanyName, out var prototype))
            {
                // Don't include "None" companies in the IFF label
                if (prototype.ID != "None")
                {
                    companyName = prototype.Name;
                    companyColor = prototype.Color;
                }
            }
            else
            {
                // For unknown companies, still check if it's not "None"
                if (companyComp.CompanyName != "None")
                {
                    companyName = companyComp.CompanyName;
                    companyColor = Color.Yellow;
                }
            }
        }

        var labelText = string.IsNullOrEmpty(entName) ? Loc.GetString("shuttle-console-unknown") : entName;

        // Add company info if available
        if (companyName != null && companyColor != null)
        {
            // Return a formatted label that the client can parse properly
            return $"{labelText}\n{Loc.GetString(companyName)}"; // Ru-Localization
        }

        return labelText;
    }

    /// <summary>
    /// Sets the color for this grid to appear as on radar.
    /// </summary>
    [PublicAPI]
    public void SetIFFColor(EntityUid gridUid, Color color, IFFComponent? component = null)
    {
        component ??= EnsureComp<IFFComponent>(gridUid);

        if (component.ReadOnly) // Frontier: POI IFF protection
            return; // Frontier: POI IFF protection

        if (component.Color.Equals(color))
            return;

        component.Color = color;
        Dirty(gridUid, component);
        UpdateIFFInterfaces(gridUid, component);
    }

    [PublicAPI]
    public void AddIFFFlag(EntityUid gridUid, IFFFlags flags, IFFComponent? component = null)
    {
        component ??= EnsureComp<IFFComponent>(gridUid);

        if (component.ReadOnly) // Frontier: POI IFF protection
            return; // Frontier: POI IFF protection

        if ((component.Flags & flags) == flags)
            return;

        component.Flags |= flags;
        Dirty(gridUid, component);
        UpdateIFFInterfaces(gridUid, component);
    }

    [PublicAPI]
    public void RemoveIFFFlag(EntityUid gridUid, IFFFlags flags, IFFComponent? component = null)
    {
        if (!Resolve(gridUid, ref component, false))
            return;

        if (component.ReadOnly) // Frontier: POI IFF protection
            return; // Frontier: POI IFF protection

        if ((component.Flags & flags) == 0x0)
            return;

        component.Flags &= ~flags;
        Dirty(gridUid, component);
        UpdateIFFInterfaces(gridUid, component);
    }

    // Frontier: POI IFF protection
    [PublicAPI]
    public void SetIFFReadOnly(EntityUid gridUid, bool readOnly, IFFComponent? component = null)
    {
        if (!Resolve(gridUid, ref component, false))
            return;

        if (component.ReadOnly == readOnly)
            return;

        component.ReadOnly = readOnly;
    }
    // End Frontier
}
