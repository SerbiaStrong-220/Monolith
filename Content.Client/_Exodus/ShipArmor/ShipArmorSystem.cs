// (c) Space Exodus Team
using Content.Shared._Exodus.ShipArmor;
using Content.Shared.FixedPoint;
using Robust.Client.GameObjects;

namespace Content.Client._Exodus.ShipArmor;

/// <summary>
/// Client half of ship armor — examine/state only; absorption runs on the server.
/// </summary>
public sealed class ShipArmorSystem : SharedShipArmorSystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShipArmorComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
    }

    private void OnAfterAutoHandleState(Entity<ShipArmorComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<SpriteComponent>(ent.Owner, out var sprite))
            return;

        var stage = GetVisualStage(ent.Comp);
        sprite.LayerSetState(0, $"armor_stage_{stage}");
    }

    private static int GetVisualStage(ShipArmorComponent armor)
    {
        if (armor.MaxCharge <= FixedPoint2.Zero)
            return 8;

        var chargeRatio = Math.Clamp(armor.CurrentCharge.Float() / armor.MaxCharge.Float(), 0f, 1f);
        return Math.Clamp((int)((1f - chargeRatio) * 9f), 0, 8);
    }
}
