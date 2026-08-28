using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Raised on a grid when its corporate controller changes.
/// </summary>
[ByRefEvent]
public readonly record struct GridTerritoryCorporateControllerChangedEvent(
    EntityUid Grid,
    ProtoId<CompanyPrototype>? OldCompany,
    ProtoId<CompanyPrototype>? NewCompany,
    EntityUid? OldSourceBanner,
    EntityUid? SourceBanner,
    EntityUid? Actor);
