// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 pheenty <fedorlukin2006@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Goobstation.MantisBlades;
using Content.Server.Emp;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Emp;
using Content.Shared.Hands.Components;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Goobstation.PairedExtendable.Systems;

public sealed partial class MantisBladesSystem : EntitySystem // Exodus: generated dependency injection.
{
    [Dependency] private SharedActionsSystem _actions = default!; // Exodus: generated dependency injection.
    [Dependency] private SharedAudioSystem _audio = default!; // Exodus: generated dependency injection.
    [Dependency] private SharedPopupSystem _popup = default!; // Exodus: generated dependency injection.
    [Dependency] private PairedExtendableSystem _pairedExtendable = default!; // Exodus: generated dependency injection.
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MantisBladeArmComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<MantisBladeArmComponent, BodyPartAddedEvent>(OnAttach);
        SubscribeLocalEvent<MantisBladeArmComponent, ToggleMantisBladeEvent>(OnToggle);
        SubscribeLocalEvent<MantisBladeArmComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MantisBladeArmComponent, BodyPartRemovedEvent>(OnDetach);
        SubscribeLocalEvent<MantisBladeArmComponent, EmpPulseEvent>(OnEmpPulse);
    }

    private void OnInit(Entity<MantisBladeArmComponent> ent, ref ComponentInit args) => AddAction(ent);

    private void OnAttach(Entity<MantisBladeArmComponent> ent, ref BodyPartAddedEvent args) => AddAction(ent);

    private void AddAction(Entity<MantisBladeArmComponent> ent)
    {
        if (!TryComp<BodyPartComponent>(ent, out var part)
            || part.Body == null)
            return;

        ent.Comp.ActionUid = _actions.AddAction(part.Body.Value, ent.Comp.ActionProto, ent);
    }

    private void OnToggle(Entity<MantisBladeArmComponent> ent, ref ToggleMantisBladeEvent args)
    {
        if (!TryComp<BodyPartComponent>(ent, out var part)
        || part.Body == null)
            return;

        if (HasComp<EmpDisabledComponent>(ent))
        {
            _popup.PopupEntity(Loc.GetString("mantis-blade-disabled-emp"), ent, part.Body.Value);
            return;
        }

        var handLocation = part.Symmetry switch
        {
            BodyPartSymmetry.Left => HandLocation.Left,
            BodyPartSymmetry.Right => HandLocation.Right,
            BodyPartSymmetry.None => HandLocation.Middle,
            _ => throw new ArgumentOutOfRangeException(),
        };

        args.Handled = _pairedExtendable.ToggleExtendable(part.Body.Value,
            ent.Comp.BladeProto,
            handLocation,
            out ent.Comp.BladeUid,
            ent.Comp.BladeUid);

        if (args.Handled)
            _audio.PlayPvs(ent.Comp.BladeUid == null ? ent.Comp.RetractSound : ent.Comp.ExtendSound, ent);
    }

    private void OnShutdown(Entity<MantisBladeArmComponent> ent, ref ComponentShutdown args)
    {
        Del(ent.Comp.BladeUid);
        Del(ent.Comp.ActionUid);
    }

    private void OnDetach(Entity<MantisBladeArmComponent> ent, ref BodyPartRemovedEvent args)
    {
        Del(ent.Comp.BladeUid);
        Del(ent.Comp.ActionUid);
    }

    private void OnEmpPulse(EntityUid uid, MantisBladeArmComponent comp, ref EmpPulseEvent args)
    {
        args.Affected = true;
        args.Disabled = true;
    }

}
