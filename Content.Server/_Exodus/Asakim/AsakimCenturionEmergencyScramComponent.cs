using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Teleportation;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Asakim;

[RegisterComponent]
public sealed partial class AsakimCenturionEmergencyScramComponent : Component
{
    [DataField]
    public EntProtoId ActionProto = "ActionAsakimCenturionEmergencyScram";

    [DataField]
    public EntityUid? ActionUid;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    [DataField]
    public TimeSpan NextActivation = TimeSpan.Zero;

    [DataField]
    public List<ReagentQuantity> Reagents =
    [
        new("Omnizine", FixedPoint2.New(25)),
        new("TranexamicAcid", FixedPoint2.New(5)),
        new("Dexalin", FixedPoint2.New(5)),
        new("DexalinPlus", FixedPoint2.New(5)),
    ];

    [DataField]
    public TeleportSpecifier Specifier = new()
    {
        TeleportRadius = 40f,
        TeleportAttempts = 5,
        ForceSafe = false,
        MinRadiusFraction = 0.75f,
    };

    [DataField]
    public SoundSpecifier InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");
}
