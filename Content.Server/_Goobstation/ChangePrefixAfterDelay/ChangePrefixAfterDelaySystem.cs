// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 pheenty <fedorlukin2006@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Item;
using Robust.Shared.Timing;

namespace Content.Server._Goobstation.ChangePrefixAfterDelay;

public sealed partial class ChangePrefixAfterDelaySystem : EntitySystem // Exodus: generated dependency injection.
{
    [Dependency] private ClothingSystem _clothing = default!; // Exodus: generated dependency injection.
    [Dependency] private SharedItemSystem _item = default!; // Exodus: generated dependency injection.
    [Dependency] private IGameTiming _timing = default!; // Exodus: generated dependency injection.

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChangePrefixAfterDelayComponent, ComponentInit>(OnInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ChangePrefixAfterDelayComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ChangeAt == null
                || comp.ChangeAt > _timing.CurTime)
                continue;

            _clothing.SetEquippedPrefix(uid, comp.NewEquippedPrefix);
            _item.SetHeldPrefix(uid, comp.NewHeldPrefix);
            RemCompDeferred(uid, comp);
        }
    }

    private void OnInit(Entity<ChangePrefixAfterDelayComponent> ent, ref ComponentInit args) =>
        ent.Comp.ChangeAt = _timing.CurTime + ent.Comp.Delay;
}
