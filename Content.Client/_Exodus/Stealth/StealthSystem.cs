using System.Linq;
using Content.Client.Interactable.Components;
using Content.Client.StatusIcon;
using Content.Shared.Exodus.Stealth;
using Content.Shared.Exodus.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Exodus.Stealth;

public sealed class StealthSystem : SharedStealthSystem
{
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedStealthSystem _stealth = default!;

    private ShaderInstance _shader = default!;

    public override void Initialize()
    {
        base.Initialize();

        _shader = _protoMan.Index<ShaderPrototype>("Stealth").InstanceUnique();

        SubscribeLocalEvent<StealthComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<StealthComponent, BeforePostShaderRenderEvent>(OnShaderRender);

        SubscribeLocalEvent<StealthComponent, StealthRequestChangeEvent>(OnRequestChange);
    }


    private void OnRequestChange(EntityUid uid, StealthComponent comp, StealthRequestChangeEvent args)
    {
        var enabled = !_stealth.IsVisible(uid);
        if (enabled)
            AddShader(uid);
        else
            RemoveShader(uid);

    }

    private void AddShader(EntityUid uid, StealthComponent? component = null, SpriteComponent? sprite = null)
    {
        if (!Resolve(uid, ref component, ref sprite, false))
            return;

        _sprite.SetColor((uid, sprite), Color.White);
        sprite.PostShader = _shader;
        sprite.GetScreenTexture = true;
        sprite.RaiseShaderEvent = true;

        if (TryComp(uid, out InteractionOutlineComponent? outline))
        {
            RemCompDeferred(uid, outline);
            component.HadOutline = true;
        }
    }

    private void RemoveShader(EntityUid uid, StealthComponent? component = null, SpriteComponent? sprite = null)
    {
        if (!Resolve(uid, ref component, ref sprite, false))
            return;

        _sprite.SetColor((uid, sprite), Color.White);
        sprite.PostShader = null;
        sprite.GetScreenTexture = false;
        sprite.RaiseShaderEvent = false;

        if (component.HadOutline && !TerminatingOrDeleted(uid))
        {
            EnsureComp<InteractionOutlineComponent>(uid);
        }
    }

    private void OnShutdown(EntityUid uid, StealthComponent component, ComponentShutdown args)
    {
        if (!Terminating(uid))
            RemoveShader(uid);
    }

    private void OnShaderRender(EntityUid uid, StealthComponent component, BeforePostShaderRenderEvent args)
    {
        // Distortion effect uses screen coordinates. If a player moves, the entities appear to move on screen. this
        // makes the distortion very noticeable.

        // So we need to use relative screen coordinates. The reference frame we use is the parent's position on screen.
        // this ensures that if the Stealth is not moving relative to the parent, its relative screen position remains
        // unchanged.
        var parent = Transform(uid).ParentUid;
        if (!parent.IsValid())
            return; // should never happen, but lets not kill the client.
        var parentXform = Transform(parent);
        var reference = args.Viewport.WorldToLocal(_transformSystem.GetWorldPosition(parentXform));
        reference.X = -reference.X;
        var visibility = GetVisibility(uid, component);

        // actual visual visibility effect is limited to +/- 1.5.
        visibility = Math.Clamp(visibility, -1.5f, 1f);

        _shader.SetParameter("reference", reference);
        _shader.SetParameter("visibility", visibility);

        visibility = MathF.Max(0, visibility);
        _sprite.SetColor((uid, args.Sprite), new Color(visibility, visibility, 1, 1));
    }
}
