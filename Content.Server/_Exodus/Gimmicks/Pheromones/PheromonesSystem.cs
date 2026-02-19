// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using System.Diagnostics.CodeAnalysis;
using Content.Server.Actions;
using Content.Server.EUI;
using Content.Server.Forensics;
using Content.Server.Interaction;
using Content.Server.Popups;
using Content.Shared.Examine;
using Content.Shared.Exodus.Gimmicks.Bioluminescence;
using Content.Shared.Exodus.Gimmicks.Pheromones;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Exodus.Gimmicks.Pheromones;

public sealed partial class PheromonesSystem : SharedPheromonesSystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ForensicsSystem _forensics = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly InteractionSystem _interaction = default!;

    #region Public API

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(BioluminescenceSystem));

        SubscribeLocalEvent<PheromonesCommunicationComponent, MapInitEvent>(OnStartup);
        SubscribeLocalEvent<PheromonesCommunicationComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<TryMarkWithPheromonesEvent>(OnTryMarkWithPheromones);
        SubscribeLocalEvent<PheromonesComponent, ExaminedEvent>(OnExamined);
    }

    public void MarkTargetWithPheromones(EntityUid target, EntityUid user, string text)
    {
        if (!ValidateCanMark(user, target, out var communication))
            return;

        if (HasComp<PheromonesComponent>(target))
        {
            _popup.PopupClient(Loc.GetString("pheromones-component-already-marked-popup"), user);
            return;
        }

        _forensics.TransferDna(target, user);
        var pheromones = AddComp<PheromonesComponent>(target);
        pheromones.Color = communication.Color;
        pheromones.Text = text;
        _popup.PopupClient(Loc.GetString("pheromones-component-success-popup"), user);
    }

    public void MarkCoordsWithPheromones(EntityCoordinates target, EntityUid user, string text)
    {
        if (!ValidateCanMark(user, target, out var communication))
            return;

        var cloud = Spawn(communication.PheromonesCloud, target);
        var pheromones = EnsureComp<PheromonesComponent>(cloud);
        pheromones.Color = communication.Color;
        pheromones.Text = text;
        _forensics.TransferDna(cloud, user);
        _popup.PopupClient(Loc.GetString("pheromones-component-success-popup"), user);
    }

    public bool ValidateCanMark(EntityUid user, EntityUid target, bool silent = false)
    {
        if (!TryComp<PheromonesCommunicationComponent>(user, out var communication))
            return false;

        if (!_interaction.InRangeAndAccessible(user, target, communication.Range.Value))
        {
            if (!silent)
            {
                _popup.PopupClient(Loc.GetString("pheromones-component-too-far-popup"), user);
            }

            return false;
        }

        return true;
    }
    public bool ValidateCanMark(EntityUid user, EntityCoordinates target, bool silent = false)
    {
        if (!TryComp<PheromonesCommunicationComponent>(user, out var communication))
            return false;

        if (!_interaction.InRangeUnobstructed(user, target, communication.Range.Value))
        {
            if (!silent)
            {
                _popup.PopupClient(Loc.GetString("pheromones-component-too-far-popup"), user);
            }

            return false;
        }

        return true;
    }
    public bool ValidateCanMark(EntityUid user, EntityUid target, [NotNullWhen(true)] out PheromonesCommunicationComponent? communication, bool silent = false)
    {
        if (!TryComp(user, out communication))
            return false;

        if (!_interaction.InRangeAndAccessible(user, target, communication.Range.Value))
        {
            if (!silent)
            {
                _popup.PopupClient(Loc.GetString("pheromones-component-too-far-popup"), user);
            }

            return false;
        }

        return true;
    }
    public bool ValidateCanMark(EntityUid user, EntityCoordinates target, [NotNullWhen(true)] out PheromonesCommunicationComponent? communication, bool silent = false)
    {
        if (!TryComp(user, out communication))
            return false;

        if (!_interaction.InRangeUnobstructed(user, target, communication.Range.Value))
        {
            if (!silent)
            {
                _popup.PopupClient(Loc.GetString("pheromones-component-too-far-popup"), user);
            }

            return false;
        }

        return true;
    }

    #endregion

    #region Private API
    private void OnExamined(Entity<PheromonesComponent> entity, ref ExaminedEvent args)
    {
        if (!HasComp<PheromonesCommunicationComponent>(args.Examiner))
            return;

        args.PushMarkup(
            Loc.GetString("pheromones-component-examined-markup",
                ("text", FormattedMessage.EscapeText(entity.Comp.Text)),
                ("color", entity.Comp.Color)
            )
        );
    }

    private void OnTryMarkWithPheromones(TryMarkWithPheromonesEvent args)
    {
        if (!TryComp<ActorComponent>(args.Performer, out var actor))
            return;

        args.Handled = true;

        var eui = new PheromonesAskEui(this, args.Entity, args.Coords);
        _eui.OpenEui(eui, actor.PlayerSession);
    }

    private void OnStartup(Entity<PheromonesCommunicationComponent> entity, ref MapInitEvent args)
    {
        _actions.AddAction(entity, ref entity.Comp.ActionEntity, entity.Comp.ActionPrototype);

        if (TryComp<BioluminescenceComponent>(entity, out var bioluminescence))
        {
            entity.Comp.Color = bioluminescence.Color;
        }
    }

    private void OnShutdown(Entity<PheromonesCommunicationComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.ActionEntity != null && entity.Comp.ActionEntity.Value.IsValid())
        {
            _actions.RemoveAction(entity.Comp.ActionEntity);
        }
    }

    #endregion
}
