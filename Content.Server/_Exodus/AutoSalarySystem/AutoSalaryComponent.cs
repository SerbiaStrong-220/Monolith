// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
namespace Content.Server.Exodus.AutoSalarySystem;

[RegisterComponent]
public sealed partial class AutoSalaryComponent : Component
{
    [DataField]
    public TimeSpan LastSalaryAt = TimeSpan.Zero;
}
