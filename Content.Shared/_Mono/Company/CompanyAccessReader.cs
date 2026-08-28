using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.Company;

/// <summary>
/// Component that checks if an entity belongs to a specific company before granting access.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(CompanyAccessReaderSystem))]
public sealed partial class CompanyAccessReaderComponent : Component
{
    /// <summary>
    /// The company ID that is required to access this entity.
    /// </summary>
    [DataField("requiredCompanies")]
    public List<ProtoId<CompanyPrototype>> RequiredCompanies = [];

    [DataField]
    public bool Inverted = false;

    // Exodus-begin company-card-access
    /// <summary>
    /// Requires the user to present an ID card with a company assigned. An empty
    /// <see cref="RequiredCompanies"/> list accepts any company card.
    /// </summary>
    [DataField] // Exodus-company-card-access
    public bool RequireCompanyCard = false;
    // Exodus-end company-card-access

    /// <summary>
    /// Message to show when access is denied due to incorrect company.
    /// </summary>
    [DataField("popupMessage")]
    public string? PopupMessage = "company-tsf-access-denied";
}
