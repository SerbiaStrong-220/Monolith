// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Shared._Exodus.GameTicking.Requirements;

namespace Content.Server._Exodus.GameTicking.Requirements;

[RegisterComponent]
public sealed partial class GameRuleRequirementsComponent : Component
{
    [DataField]
    public List<GameRuleRequirement> Requirements = new();
}
