namespace Content.Server._Exodus.Silicons.Borgs;

[RegisterComponent]
public sealed partial class BorgModuleStackRechargerComponent : Component
{
    [DataField]
    public TimeSpan RechargeInterval = TimeSpan.FromSeconds(45);

    [DataField]
    public int RechargeAmount = 1;

    [DataField]
    public int MaxCount = 10;

    [DataField]
    public TimeSpan NextRecharge = TimeSpan.Zero;
}
