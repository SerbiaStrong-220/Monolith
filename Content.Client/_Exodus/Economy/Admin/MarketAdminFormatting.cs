using System.Globalization;

namespace Content.Client._Exodus.Economy.Admin;

/// <summary>
/// Compact localized market numbers and conversions between saved values and displayed percentages.
/// </summary>
public static class MarketAdminFormatting
{
    public static string Number(double value, CultureInfo culture, bool signed = false)
    {
        if (value == 0)
            return "0";

        var magnitude = Math.Abs(value);
        string formatted;
        if (magnitude is > 0 and < 0.01)
        {
            // Preserve at most three significant digits for small gas pressures, using scientific
            // notation only when ordinary notation would no longer make a compact input field.
            if (magnitude < 1e-9)
                formatted = value.ToString("G3", culture);
            else
            {
                var decimals = 2 - (int) Math.Floor(Math.Log10(magnitude));
                formatted = value.ToString("0." + new string('#', decimals), culture);
            }
        }
        else
        {
            formatted = value.ToString("0.##", culture);
        }

        return signed && value > 0 ? culture.NumberFormat.PositiveSign + formatted : formatted;
    }

    public static string Factor(double value, CultureInfo culture)
    {
        return value.ToString("G12", culture);
    }

    public static double FactorChange(double factor)
    {
        return (factor - 1) * 100;
    }

    public static double RisePercent(double strength, double reference)
    {
        if (!double.IsFinite(strength) || strength < 0 || !double.IsFinite(reference) || reference <= 0)
            return double.NaN;

        return 100 * ExpMinusOne(strength / reference);
    }

    public static double FallPercent(double strength, double reference)
    {
        if (!double.IsFinite(strength) || strength < 0 || !double.IsFinite(reference) || reference <= 0)
            return double.NaN;

        return 100 * ExpMinusOne(-strength / reference);
    }

    public static double StrengthFromPercent(double percent, double reference)
    {
        if (!double.IsFinite(percent) || percent < 0 || !double.IsFinite(reference) || reference <= 0)
            return double.NaN;

        var fraction = percent / 100;
        var sum = 1 + fraction;
        // Correct for the rounded addition; a fraction below one's precision is already its own
        // logarithm to double precision. Runtime LogP1 implementations can lose these small values.
        var logarithm = sum == 1 ? fraction : Math.Log(sum) * (fraction / (sum - 1));
        return reference * logarithm;
    }

    private static double ExpMinusOne(double value)
    {
        if (Math.Abs(value) >= 0.01)
            return Math.Exp(value) - 1;

        // Avoid subtracting two nearly equal numbers for gas pressure. The first omitted Taylor
        // term is x^8/40320, below double precision throughout this interval.
        return value * (1 + value * (0.5 + value * (1.0 / 6 + value * (1.0 / 24 +
            value * (1.0 / 120 + value * (1.0 / 720 + value / 5040))))));
    }
}
