namespace Content.Server._Exodus.Asakim;

/// <summary>
/// Attached to clothing biocoded to Asakim wearers. When the clothing detects a non-Asakim
/// (and non-trusted) live wearer — either equipped while alive, or revived from
/// Dead/Critical → Alive — it self-destructs via the standard trigger chain, taking the
/// wearer with it. No-op while the wearer is dead or in crit.
/// </summary>
[RegisterComponent, Access(typeof(AsakimSuitBiocheckSystem))]
public sealed partial class AsakimSuitBiocheckComponent : Component;
