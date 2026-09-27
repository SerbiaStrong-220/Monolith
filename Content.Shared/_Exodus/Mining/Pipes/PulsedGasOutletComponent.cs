using System.Numerics;
using Content.Shared.Atmos;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Mining.Pipes;

/// <summary>A pipe-fed outlet that discharges a bounded gas pulse at a configurable nozzle position.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class PulsedGasOutletComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField]
    public string Inlet = "pipe";

    [DataField]
    public TimeSpan CycleInterval = TimeSpan.FromSeconds(27);

    [DataField]
    public float MolesPerPulse = 35;

    [DataField]
    public float MaxPressure = 2 * Atmospherics.MaxOutputPressure;

    /// <summary>Gas is released just beyond the nozzle, in entity-local coordinates.</summary>
    [DataField]
    public Vector2 OutletOffset = new(0, 1.6f);

    [DataField]
    public TimeSpan EffectDuration = TimeSpan.FromSeconds(4);

    [DataField]
    public SoundSpecifier? DischargeSound = new SoundPathSpecifier("/Audio/Ambience/Objects/gas_hiss.ogg")
    {
        Params = AudioParams.Default.WithVolume(-8),
    };

    [DataField, AutoPausedField]
    public TimeSpan NextPulse;

    /// <summary>Only successful gas transfers start a visible plume; late observers resume its current frame.</summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan? LastPulse;
}

public enum PulsedGasOutletVisualLayers : byte
{
    Body,
    Indicator,
    Plume,
}
