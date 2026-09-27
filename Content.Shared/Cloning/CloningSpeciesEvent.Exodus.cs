// Exodus: appearance disguises can retain the original body's species when cloned.
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Cloning;

[ByRefEvent]
public record struct CloningSpeciesEvent(ProtoId<SpeciesPrototype> Species);
