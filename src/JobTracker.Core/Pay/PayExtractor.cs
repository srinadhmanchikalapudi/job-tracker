using System.Globalization;
using System.Text.RegularExpressions;

namespace JobTracker.Core.Pay;

/// <summary>
/// Finds the salary, pay range or hourly or daily rate in a pasted job posting. Plain rules, no AI: the same text always gives the same answer, and
/// the line it came from is kept. Money that is not pay (company revenue, funding, a sign-on bonus, a stipend, a 401(k) match) is left out, and a
/// posting that says only "competitive" gives nothing, so the answer is "not available" rather than a guess.
/// </summary>
public static partial class PayExtractor
{
    private const string Symbol = @"US\$|CA\$|C\$|AU\$|A\$|\$|£|€|₹|USD|CAD|GBP|EUR|AUD|INR";
    private const string Number = @"\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?";

    // "$120,000", "US$55", "£50k", "USD 120,000": a currency, then an amount.
    private static readonly Regex Money = new(
        $@"(?<cur>{Symbol})\s?(?<n>{Number})(?<k>\s?[kK](?![A-Za-z]))?(?<plus>\+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The second end of a range: "- $150,000", "to 150k", "and $60".
    private static readonly Regex RangeTail = new(
        $@"^\s*(?:-|–|—|to|and|through|thru)\s*(?<cur>{Symbol})?\s?(?<n>{Number})(?<k>\s?[kK](?![A-Za-z]))?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "50 - 60 USD/hr", "45-55 per hour": no symbol, so it needs the unit right after to count as pay.
    private static readonly Regex Bare = new(
        $@"(?<![\d,.$£€₹])(?<n>{Number})(?<k>\s?[kK](?![A-Za-z]))?\s*(?:-|–|—|to)\s*(?<n2>{Number})(?<k2>\s?[kK](?![A-Za-z]))?\s*(?<cur>USD|CAD|GBP|EUR|AUD|INR)?\s*(?=(?:/|per\s|an?\s|each\s|hourly|annually|yearly|monthly|weekly|daily)\s*(?:hr|hour|hourly|yr|year|annum|annual|month|week|wk|day|h\b)|hourly|annually|yearly)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly (Regex Pattern, PayUnit Unit)[] UnitMarkers =
    [
        (new Regex(@"^\s*(?:/|per\s+|an?\s+|each\s+|p\.?\s?)?\s*(?:hr\b|hour|hourly|h\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled), PayUnit.Hour),
        (new Regex(@"^\s*(?:/|per\s+|an?\s+|each\s+)?\s*(?:day\b|daily|diem)", RegexOptions.IgnoreCase | RegexOptions.Compiled), PayUnit.Day),
        (new Regex(@"^\s*(?:/|per\s+|an?\s+|each\s+)?\s*(?:wk\b|week|weekly)", RegexOptions.IgnoreCase | RegexOptions.Compiled), PayUnit.Week),
        (new Regex(@"^\s*(?:/|per\s+|an?\s+|each\s+)?\s*(?:mo\b|month|monthly)", RegexOptions.IgnoreCase | RegexOptions.Compiled), PayUnit.Month),
        (new Regex(@"^\s*(?:/|per\s+|an?\s+|each\s+)?\s*(?:yr\b|year|yearly|annum|annual|p\.a\.)", RegexOptions.IgnoreCase | RegexOptions.Compiled), PayUnit.Year),
    ];

    private static readonly Regex PayWords = new(
        @"salary|salaries|compensation|\bpay\b|pay\s*range|payrange|\bwage|\brate\b|\bbase\b|\bearn|hourly|per\s+hour|annual|remuneration|\bOTE\b|\bgross\b|\bpackage\b|range",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Money that comes right after these words ("a sign-on bonus of $10,000", "company match up to $6,000") is about the company or an extra, not the pay.
    private static readonly Regex NotPayBefore = new(
        @"revenue|funding|funded|raised|valuation|\bARR\b|\bIPO\b|\bin sales\b|budget|portfolio|assets|investment|investors?|series\s+[a-f]\b|sign(?:ing)?[\s-]*on|signing|bonus|stipend|relocation|allowance|per\s+diem|equity|stock|\bRSU|401|\bHSA\b|reimburs|tuition|\bmatch|referral|commission|gift|fee\b|deductible|premium|grant",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ...and money that comes right before them ("$10,000 sign-on bonus", "$200,000 in equity"). "$140,000 + equity" is pay with an extra on top, so a plus,
    // "and" or a comma in between lets it through.
    private static readonly Regex NotPayAfter = new(
        @"^(?![ \t]*(?:\+|plus\b|and\b|,|&))\W{0,3}(?:in\s+|of\s+|worth\s+)?(?:annual\s+)?(?:sign|bonus|stipend|equity|stock|rsu|relocation|allowance|revenue|funding|assets|budget|valuation|arr\b|sales|portfolio|investment|gift|reimburs|tuition|match|commission)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Scale = new(@"^\s?(?:million|billion|trillion|M\b|B\b|MM\b|bn\b|mn\b)", RegexOptions.Compiled);

    private sealed record Candidate(decimal Min, decimal Max, PayUnit Unit, string Currency, bool UnitStated, string Qualifier, string Line, int Score, int Index);

    /// <summary>The pay stated in the posting, or null when it states none.</summary>
    public static PayInfo? Extract(string? jdText)
    {
        if (string.IsNullOrWhiteSpace(jdText))
            return null;

        var text = Normalise(jdText);
        var candidates = new List<Candidate>();

        var usedUpTo = -1;
        foreach (Match m in Money.Matches(text))
        {
            if (m.Index < usedUpTo)
                continue; // the second end of a range that was already read
            var tail = RangeTail.Match(text[(m.Index + m.Length)..Math.Min(text.Length, m.Index + m.Length + 40)]);
            if (tail.Success)
                usedUpTo = m.Index + m.Length + tail.Length;
            if (FromMoney(text, m) is { } c)
                candidates.Add(c);
        }

        foreach (Match m in Bare.Matches(text))
            if (FromBare(text, m) is { } c)
                candidates.Add(c);

        if (candidates.Count == 0)
            return null;

        var best = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Index).First();
        var others = candidates
            .Where(c => c.Score >= 4 && (c.Min != best.Min || c.Max != best.Max || c.Unit != best.Unit))
            .Select(c => (c.Min, c.Max, c.Unit)).Distinct().Count();
        return new PayInfo(best.Min, best.Max, best.Unit, best.Currency, best.UnitStated, best.Qualifier, best.Line, others);
    }

    private static string Normalise(string text) =>
        text.Replace(' ', ' ').Replace(' ', ' ').Replace(' ', ' ').Replace("\r\n", "\n").Replace('\r', '\n');

    // ---------------------------------------------------------------- one amount, with a symbol

    private static Candidate? FromMoney(string text, Match m)
    {
        var end = m.Index + m.Length;
        if (Scale.IsMatch(text[end..Math.Min(text.Length, end + 12)]))
            return null; // $5 billion, $20M

        var low = Amount(m.Groups["n"].Value, m.Groups["k"].Success);
        var high = low;
        var kOnLow = m.Groups["k"].Success;
        var currency = Currency(m.Groups["cur"].Value);

        var tail = RangeTail.Match(text[end..Math.Min(text.Length, end + 40)]);
        var consumed = end;
        if (tail.Success)
        {
            var secondK = tail.Groups["k"].Success;
            var secondRaw = Amount(tail.Groups["n"].Value, secondK);
            // "$120-150k": the k on the second figure belongs to the first as well.
            if (secondK && !kOnLow && low < 1000 && secondRaw >= 1000)
                low *= 1000;
            // "$120 - $150" with a currency on the second end must agree with the first.
            if (tail.Groups["cur"].Success && Currency(tail.Groups["cur"].Value) != currency && currency.Length > 0)
                return null;
            high = secondRaw;
            consumed = end + tail.Length;
        }

        if (high < low)
            (low, high) = (high, low);
        if (low <= 0 || (high / low) > 5m)
            return null; // not one range of one thing

        return Build(text, m.Index, consumed, low, high, currency, m.Groups["plus"].Success);
    }

    // ---------------------------------------------------------------- a range with no symbol, but a unit right after it

    private static Candidate? FromBare(string text, Match m)
    {
        var kFirst = m.Groups["k"].Success;
        var kSecond = m.Groups["k2"].Success;
        var low = Amount(m.Groups["n"].Value, kFirst);
        var high = Amount(m.Groups["n2"].Value, kSecond);
        if (kSecond && !kFirst && low < 1000 && high >= 1000)
            low *= 1000;
        if (high < low)
            (low, high) = (high, low);
        if (low <= 0 || (high / low) > 5m)
            return null;

        var currency = Currency(m.Groups["cur"].Value);
        return Build(text, m.Index, m.Index + m.Length, low, high, currency, plus: false, requireUnit: true);
    }

    // ---------------------------------------------------------------- shared checks

    private static Candidate? Build(string text, int start, int end, decimal low, decimal high, string currency, bool plus, bool requireUnit = false)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        var lineEnd = text.IndexOf('\n', end);
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;
        var line = text[lineStart..lineEnd].Trim();

        // The words just around the figures, on the same line, decide whether it is pay.
        var before = text[Math.Max(lineStart, start - 30)..start];
        var rightAfter = text[end..Math.Min(lineEnd, end + 30)];
        if (NotPayBefore.IsMatch(before) || NotPayAfter.IsMatch(rightAfter))
            return null;

        var after = text[end..Math.Min(text.Length, end + 40)];
        var (unit, stated) = StatedUnit(after);
        if (requireUnit && !stated)
            return null;

        var wide = text[Math.Max(0, start - 90)..Math.Min(text.Length, end + 60)];
        var hasPayWord = PayWords.IsMatch(wide);
        var hasCurrency = currency.Length > 0;
        if (!stated && !hasPayWord)
            return null; // "$5 off", "$1,200" on its own: no sign it is pay

        // No unit right after the figures: a word nearby ("salary", "hourly rate") says it; failing that, the size of the numbers is a last resort.
        if (!stated)
        {
            var named = UnitFromNearbyWords(wide);
            stated = named is not null;
            unit = named ?? GuessUnit(high, hasPayWord);
        }

        if (unit is null)
            return null;
        var chosen = unit.Value;

        if (!Plausible(low, high, chosen))
            return null;

        var qualifier = plus ? "from" : QualifierBefore(text, start);
        if (high > low)
            qualifier = ""; // a range already says both ends

        var score = (high > low ? 3 : 0) + (stated ? 2 : 0) + (hasPayWord ? 2 : 0) + (LabelLine.IsMatch(line) ? 2 : 0) + (hasCurrency ? 1 : 0);
        return new Candidate(low, high, chosen, currency, stated, qualifier, Shorten(line), score, start);
    }

    private static readonly Regex LabelLine = new(@"^\W*(?:base\s+)?(?:salary|pay|compensation|hourly|rate|wage|pay\s*range|salary\s*range|pay\s*rate|expected|estimated|annual)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (PayUnit? Unit, bool Stated) StatedUnit(string after)
    {
        // "$55.00 - $65.00 USD per hour": a currency code may sit between the figures and the unit.
        var rest = Regex.Replace(after, @"^\s*(?:USD|CAD|GBP|EUR|AUD|INR)\b", "", RegexOptions.IgnoreCase);
        foreach (var (pattern, unit) in UnitMarkers)
            if (pattern.IsMatch(rest))
                return (unit, true);
        return (null, false);
    }

    private static PayUnit? UnitFromNearbyWords(string window)
    {
        if (Regex.IsMatch(window, @"hourly|per\s+hour|/\s?hr|an\s+hour|hour(?:ly)?\s+rate|rate\s+per\s+hour", RegexOptions.IgnoreCase))
            return PayUnit.Hour;
        if (Regex.IsMatch(window, @"annual|per\s+year|a\s+year|yearly|per\s+annum|salary", RegexOptions.IgnoreCase))
            return PayUnit.Year;
        if (Regex.IsMatch(window, @"day\s+rate|per\s+day|daily", RegexOptions.IgnoreCase))
            return PayUnit.Day;
        if (Regex.IsMatch(window, @"monthly|per\s+month", RegexOptions.IgnoreCase))
            return PayUnit.Month;
        return null;
    }

    /// <summary>With no unit anywhere near, size decides: a few hundred is an hourly rate, tens of thousands is a yearly salary. In between is not guessed.</summary>
    private static PayUnit? GuessUnit(decimal high, bool hasPayWord)
    {
        if (!hasPayWord)
            return null;
        if (high is > 0 and <= 300)
            return PayUnit.Hour;
        return high >= 20000 ? PayUnit.Year : null;
    }

    private static bool Plausible(decimal low, decimal high, PayUnit unit) => unit switch
    {
        PayUnit.Hour => low >= 5 && high <= 1000,
        PayUnit.Day => low >= 40 && high <= 5000,
        PayUnit.Week => low >= 200 && high <= 25000,
        PayUnit.Month => low >= 800 && high <= 80000,
        _ => low >= 10000 && high <= 2_000_000,
    };

    private static string QualifierBefore(string text, int start)
    {
        var before = text[Math.Max(0, start - 22)..start];
        if (Regex.IsMatch(before, @"(?:up\s+to|maximum(?:\s+of)?|max\.?|not\s+to\s+exceed|capped\s+at)\W*$", RegexOptions.IgnoreCase))
            return "up to";
        if (Regex.IsMatch(before, @"(?:from|starting\s+at|starts\s+at|minimum(?:\s+of)?|min\.?|at\s+least|base\s+of)\W*$", RegexOptions.IgnoreCase))
            return "from";
        return "";
    }

    private static decimal Amount(string number, bool thousands)
    {
        var value = decimal.Parse(number.Replace(",", ""), CultureInfo.InvariantCulture);
        return thousands ? value * 1000 : value;
    }

    private static string Currency(string raw) => raw.ToUpperInvariant() switch
    {
        "" => "",
        "$" or "US$" => "$",
        "CA$" or "C$" => "CA$",
        "AU$" or "A$" => "A$",
        "£" => "£",
        "€" => "€",
        "₹" => "₹",
        var code => code, // USD, CAD, GBP, ...: shown after the figures
    };

    private static string Shorten(string line) => line.Length > 200 ? line[..200] + "…" : line;
}
