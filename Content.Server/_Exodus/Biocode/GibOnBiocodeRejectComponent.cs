namespace Content.Server._Exodus.Biocode;

[RegisterComponent, Access(typeof(GibOnBiocodeRejectSystem))]
public sealed partial class GibOnBiocodeRejectComponent : Component
{
    [DataField]
    public bool DeleteItems;

    [DataField]
    public bool DeleteOrgans;

    [DataField]
    public bool Gib = true;
}
