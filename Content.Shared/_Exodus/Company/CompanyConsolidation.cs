using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Company;

/// <summary>
/// Resolves legacy company identities for profiles, saved items and access checks.
/// Successors are configured in company prototypes and must point directly to a current company.
/// </summary>
public static class CompanyConsolidation
{
    public static string Normalize(string company, IPrototypeManager prototypes)
    {
        if (string.IsNullOrEmpty(company))
            return "None";

        return prototypes.TryIndex<CompanyPrototype>(company, out var prototype)
            ? prototype.Successor?.Id ?? company
            : company;
    }
}
