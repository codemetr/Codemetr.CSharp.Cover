using System.Globalization;

namespace Codemetr.CSharp.Cover;

internal static class GuardText
{
    public const double Epsilon = 1e-9;

    public static string Pct(double? value) => value is null ? "n/a" : value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
