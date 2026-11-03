using System.Globalization;
namespace Dou_GW_Studio.Helpers;
public static class Numbers
{
    // Accept a decimal comma OR point, never thousands separators or non-finite values.
    public static double Parse(string text, string label)
    {
        var value = text.Trim();
        if (value.Contains(',') && value.Contains('.')) throw new ArgumentException($"{label}: use a decimal comma or point, without thousands separators.");
        if (!double.TryParse(value.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)) throw new ArgumentException($"{label}: enter a valid number.");
        return n;
    }
    public static string F(double value, int digits = 3) => value.ToString($"F{digits}", CultureInfo.InvariantCulture);
}
