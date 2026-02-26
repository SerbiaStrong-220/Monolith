// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Shared.Exodus.GameTicking.Requirements;

namespace Content.Server.Exodus.GameTicking.Requirements;

[RegisterComponent]
public sealed partial class GameRuleRequirementsComponent : Component
{
    [DataField]
    public List<GameRuleRequirement> Requirements = new();
}
