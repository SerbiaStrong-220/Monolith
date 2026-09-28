using Content.Client.Gameplay;
using Content.Shared._Exodus.Medical;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Medical;

/// <summary>Samples held input and the patient under the cursor; the server owns all healing.</summary>
public sealed partial class MedicalBeamGunSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private InputSystem _inputSystem = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private IGameTiming _timing = default!;

    // Local input state, not replicated world state.
    private NetEntity? _sentGun;
    private bool _useWasDown;
    private TimeSpan _nextInput;
    private static readonly TimeSpan InputInterval = TimeSpan.FromSeconds(0.1);

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_timing.IsFirstTimePredicted)
            return;

        var useDown = _inputSystem.CmdStates.GetState(EngineKeyFunctions.Use) == BoundKeyState.Down;
        var pressed = useDown && !_useWasDown;
        _useWasDown = useDown;

        if (_player.LocalEntity is not { } user ||
            !TryComp<HandsComponent>(user, out var hands) || hands.ActiveHandEntity is not { } gun ||
            !TryComp<MedicalBeamGunComponent>(gun, out var config) ||
            _state.CurrentState is not GameplayStateBase screen)
        {
            StopInput();
            _nextInput = TimeSpan.Zero;
            return;
        }

        var netGun = GetNetEntity(gun);
        if (config.Mode == MedicalBeamMode.Automatic)
        {
            StopInput();
            _nextInput = TimeSpan.Zero;
            if (!pressed)
                return;

            var click = _eye.PixelToMap(_input.MouseScreenPosition);
            if (click.MapId == MapId.Nullspace)
                return;
            var patient = screen.GetDamageableClickedEntity(click);
            if (patient == user)
                patient = null;
            RaiseNetworkEvent(new MedicalBeamGunInputEvent(netGun, GetNetEntity(patient), MedicalBeamMode.Automatic));
            return;
        }

        if (!useDown || !TryComp<CombatModeComponent>(user, out var combat) || !combat.IsInCombatMode)
        {
            StopInput();
            _nextInput = TimeSpan.Zero;
            return;
        }
        if (_sentGun != null && _sentGun != netGun)
            StopInput();
        if (_timing.CurTime < _nextInput)
            return;
        _nextInput = _timing.CurTime + InputInterval;

        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        var target = mouse.MapId == MapId.Nullspace ? null : screen.GetDamageableClickedEntity(mouse);
        if (target == null || target == user)
        {
            StopInput();
            return;
        }

        RaiseNetworkEvent(new MedicalBeamGunInputEvent(netGun, GetNetEntity(target), MedicalBeamMode.Manual));
        _sentGun = netGun;
    }

    private void StopInput()
    {
        if (_sentGun is not { } gun)
            return;
        RaiseNetworkEvent(new MedicalBeamGunInputEvent(gun, null, MedicalBeamMode.Manual));
        _sentGun = null;
    }
}
