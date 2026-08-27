using Robust.Shared.Serialization;

namespace Content.Shared.Exodus.ShipShields;

[Serializable, NetSerializable]
public struct ShipShieldState(
    float baseDraw = 50000f,
    float draw = 0f,
    float maxDraw = 150000f,
    bool recharging = false,
    float overloadAccumulator = 30f,
    float healthFraction = 1f,
    bool active = false,
    bool powered = false)
{
    public float BaseDraw = baseDraw;
    public float Draw = draw;
    public float MaxDraw = maxDraw;
    public bool Recharging = recharging;
    public float OverloadAccumulator = overloadAccumulator;
    public float HealthFraction = healthFraction;
    public bool Active = active;
    public bool Powered = powered;
}
