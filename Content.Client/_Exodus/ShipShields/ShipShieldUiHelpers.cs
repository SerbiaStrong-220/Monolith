namespace Content.Client._Exodus.ShipShields;

public static class ShipShieldUiHelpers
{
    public static int GetHealthPercent(float healthFraction)
    {
        return (int)MathF.Round(Math.Clamp(healthFraction, 0f, 1f) * 100f);
    }

    public static Color GetHealthColor(float healthFraction)
    {
        var fraction = Math.Clamp(healthFraction, 0f, 1f);

        return fraction <= 0.5f
            ? Color.InterpolateBetween(Color.Red, Color.Yellow, fraction * 2f)
            : Color.InterpolateBetween(Color.Yellow, Color.Lime, (fraction - 0.5f) * 2f);
    }
}
