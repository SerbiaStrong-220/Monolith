using System.Numerics;
using Content.Client._Exodus.Animations;
using Content.Client._Exodus.StyleTools;
using Content.Client._Exodus.UserInterface;
using Content.Shared._Exodus.Maths;
using Robust.Client.Animations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Calculator.UI;

public sealed class CalculatorStyle : QuickStyle
{
    public Animation OpenAnimation { get; } = new AnimationBuilder(0.4f)
        .WithControlPropertyTrack<Vector2>(nameof(CalculatorMenu.BodyOffset), x => x
            .WithEasing(Easing.OutQuint)
            .From(new Vector2(0, -100))
            .To(Vector2.Zero))
        .WithControlPropertyTrack<Color>(nameof(Control.Modulate), x => x
            .WithEasing(Easing.OutQuint)
            .From(Color.White.WithAlpha(0))
            .To(Color.White))
        .Build();

    public Animation CloseAnimation { get; } = new AnimationBuilder(0.2f)
        .WithControlPropertyTrack<Vector2>(nameof(CalculatorMenu.BodyOffset), x => x
            .WithEasing(Easing.OutCubic)
            .From(Vector2.Zero)
            .To(new Vector2(0, -100)))
        .WithControlPropertyTrack<Color>(nameof(Control.Modulate), x => x
            .WithEasing(Easing.OutExpo)
            .From(Color.White)
            .To(Color.White.WithAlpha(0)))
        .Build();

    public Animation PressAnimation(Vector2 to) => new AnimationBuilder(0.2f)
        .WithControlPropertyTrack<Vector2>(nameof(CalculatorMenu.BodyOffset), x => x
            .WithEasing(Easing.OutCubic)
            .From(Vector2.Zero)
            .AppendKeyFrame(to, AnimationTiming.Relative(0.3f))
            .To(Vector2.Zero))
        .Build();

    protected override void CreateRules()
    {
        Builder
            .Element<PanelContainer>()
            .Class("CalculatorPanel")
            .Prop(PanelContainer.StylePropertyPanel,
                StrechedStyleBoxTexture(Tex("/Textures/_Exodus/Interface/Calculator/calculator-body.png")));

        Builder
            .Element<Label>()
            .Class("CalculatorDisplay")
            .Prop(Label.StylePropertyFont, SpriteFont("CalculatorDisplayFont"));

        AddButtonStyle("CalculatorButtonDigit1", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "1");
        AddButtonStyle("CalculatorButtonDigit2", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "2");
        AddButtonStyle("CalculatorButtonDigit3", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "3");
        AddButtonStyle("CalculatorButtonDigit4", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "4");
        AddButtonStyle("CalculatorButtonDigit5", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "5");
        AddButtonStyle("CalculatorButtonDigit6", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "6");
        AddButtonStyle("CalculatorButtonDigit7", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "7");
        AddButtonStyle("CalculatorButtonDigit8", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "8");
        AddButtonStyle("CalculatorButtonDigit9", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "9");
        AddButtonStyle("CalculatorButtonDot", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "dot");
        AddButtonStyle("CalculatorButtonPlus", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "plus");
        AddButtonStyle("CalculatorButtonMinus", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "minus");
        AddButtonStyle("CalculatorButtonMultiply", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "multiply");
        AddButtonStyle("CalculatorButtonDivide", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "divide");
        AddButtonStyle("CalculatorButtonClear", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "c");
        AddButtonStyle("CalculatorButtonClearEntry", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "ce");
        AddButtonStyle("CalculatorButtonClose", "/Textures/_Exodus/Interface/Calculator/buttons.rsi", "power");

        AddButtonStyle("CalculatorButtonDigit0", "/Textures/_Exodus/Interface/Calculator/buttons-long.rsi", "0");
        AddButtonStyle("CalculatorButtonEquals", "/Textures/_Exodus/Interface/Calculator/buttons-long.rsi", "equals");
    }

    private void AddButtonStyle(string className, string rsiPath, string state)
    {
        Builder
            .Element<SpriteButton>()
                .Class(className)
                .Prop(SpriteButton.StylePropertySprite, Sprite(rsiPath, state))
            .Element<SpriteButton>()
                .Class(className)
                .Pseudo(SpriteButton.StylePseudoClassPressed)
                .Prop(SpriteButton.StylePropertySprite, Sprite(rsiPath, $"{state}_pressed"));
    }
}
