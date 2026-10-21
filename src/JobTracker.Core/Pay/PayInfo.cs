using System.Globalization;
using System.Text.Json.Serialization;

namespace JobTracker.Core.Pay;

public enum PayUnit
{
    Hour,
    Day,
    Week,
    Month,
    Year,
}

/// <summary>
/// The pay a job posting states. Every part comes from the posting's own words.
/// </summary>
/// <param name="Min">The lower figure, or the only figure.</param>
/// <param name="Max">The upper figure; equal to <paramref name="Min"/> for a single amount.</param>
/// <param name="Currency">A symbol ("$", "£", "CA$") that goes in front, or a code ("USD") that goes after; empty when the posting gives none.</param>
/// <param name="UnitStated">False when the posting does not say per hour or per year and the unit was assumed from the size of the numbers.</param>
/// <param name="Qualifier">"up to", "from", or empty.</param>
/// <param name="Source">The line of the posting the figures came from.</param>
/// <param name="OtherRanges">How many other, different pay figures the posting also lists (for example one per city).</param>
public sealed record PayInfo(decimal Min, decimal Max, PayUnit Unit, string Currency, bool UnitStated, string Qualifier, string Source, int OtherRanges)
{
    public const string NotAvailable = "Not available";

    [JsonIgnore]
    public bool IsRange => Max > Min;

    private static string UnitWords(PayUnit unit) => unit switch
    {
        PayUnit.Hour => "per hour",
        PayUnit.Day => "per day",
        PayUnit.Week => "per week",
        PayUnit.Month => "per month",
        _ => "per year",
    };

    private string Money(decimal value)
    {
        var number = value % 1 == 0 ? value.ToString("N0", CultureInfo.InvariantCulture) : value.ToString("N2", CultureInfo.InvariantCulture);
        var isCode = Currency.Length > 0 && Currency.All(char.IsLetter) && Currency.Length == 3;
        return isCode ? number : Currency + number;
    }

    private string CodeSuffix => Currency.Length == 3 && Currency.All(char.IsLetter) ? " " + Currency : "";

    /// <summary>"$120,000 – $150,000 per year", "From $55 per hour", "$50 – $60 per hour (unit not stated, assumed)".</summary>
    [JsonIgnore]
    public string Display
    {
        get
        {
            var amount = IsRange ? $"{Money(Min)} – {Money(Max)}" : Money(Min);
            var qualifier = Qualifier.Length > 0 ? char.ToUpperInvariant(Qualifier[0]) + Qualifier[1..] + " " : "";
            var text = $"{qualifier}{amount}{CodeSuffix} {UnitWords(Unit)}";
            return UnitStated ? text : text + " (unit not stated, assumed)";
        }
    }

    /// <summary>The short form for a list: "$60–$70/hr", "$140k–$170k/yr", "Up to $85/hr".</summary>
    [JsonIgnore]
    public string Short
    {
        get
        {
            string Amount(decimal value)
            {
                if (Unit == PayUnit.Year && value >= 1000 && value % 100 == 0)
                {
                    var thousands = value / 1000;
                    var text = thousands % 1 == 0 ? thousands.ToString("0", CultureInfo.InvariantCulture) : thousands.ToString("0.0", CultureInfo.InvariantCulture);
                    return (Currency.Length == 3 && Currency.All(char.IsLetter) ? "" : Currency) + text + "k";
                }

                return Money(value);
            }

            var unit = Unit switch { PayUnit.Hour => "/hr", PayUnit.Day => "/day", PayUnit.Week => "/wk", PayUnit.Month => "/mo", _ => "/yr" };
            var amount = IsRange ? $"{Amount(Min)}–{Amount(Max)}" : Amount(Min);
            var qualifier = Qualifier.Length > 0 ? char.ToUpperInvariant(Qualifier[0]) + Qualifier[1..] + " " : "";
            return $"{qualifier}{amount}{CodeSuffix}{unit}";
        }
    }

    /// <summary>For an hourly, daily, weekly or monthly rate: roughly what that comes to in a year of full-time work. Null for a yearly figure.</summary>
    [JsonIgnore]
    public string? YearlyEquivalent
    {
        get
        {
            if (Unit == PayUnit.Year)
                return null;
            var factor = Unit switch { PayUnit.Hour => 2080m, PayUnit.Day => 260m, PayUnit.Week => 52m, _ => 12m };
            var basis = Unit switch
            {
                PayUnit.Hour => "40 hours a week for 52 weeks",
                PayUnit.Day => "260 working days",
                PayUnit.Week => "52 weeks",
                _ => "12 months",
            };
            var lo = Math.Round(Min * factor);
            var hi = Math.Round(Max * factor);
            var amount = hi > lo ? $"{Money(lo)} – {Money(hi)}" : Money(lo);
            return $"About {amount}{CodeSuffix} per year ({basis})";
        }
    }
}
