namespace Content.Server._Exodus.Mining;

[RegisterComponent]
public sealed partial class OreDropModifierComponent : Component
{
    [DataField(required: true)]
    public float Modifier = 1;
}
