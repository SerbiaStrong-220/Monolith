namespace Content.Client._Exodus.ShipShields;

public static class ShipShieldUiHelpers
{
    public static int GetHealthPercent(float healthFraction)
    {
        return (int)MathF.Round(Math.Clamp(healthFraction, 0f, 1f) * 100f);
    }

    public static Color GetHealthColor(float healthFraction)
    {
        if (healthFraction <= 0.25f)
            return Color.Red;

        return healthFraction <= 0.5f
            ? Color.Orange
            : Color.Green;
    }
}
