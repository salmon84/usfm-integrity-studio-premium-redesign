using System.Text.RegularExpressions;

namespace UsfmTools.Text;

public static class ScripturePunctuationNormalizer
{
    private static readonly Regex UsfmVerseMarkerWithTrailingPunctuationRegex = new(
        @"(\\v\s*[0-9۰-۹٠-٩]+)\s*[.۔:]+\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex RedundantFullStopAfterQuestionOrExclamationRegex = new(
        @"([!?؟])[.۔]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SpaceBeforePunctuationRegex = new(
        @"\s+([,.;:!?،؛؟۔])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SpaceAfterOpeningParenthesisRegex = new(
        @"\(\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SpaceBeforeClosingParenthesisRegex = new(
        @"\s+\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MissingSpaceBeforeOpeningParenthesisRegex = new(
        @"(?<=[\p{L}\p{M}\p{N}.!؟?۔])\((?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MixedSingleQuotePairOpeningRegex = new(
        @"(?<=(?:^|[\s,،:؛;.!?؟۔]))’‘(?=\s*[^\s.۔,،؛;:!?؟])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MixedSingleQuotePairClosingRegex = new(
        @"(?<=\S)\s*’‘(?=[.۔,،؛;:!?؟\s]|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MissingSpaceAfterExclamationOrQuestionRegex = new(
        @"([!?؟])(?=[\p{L}\p{M}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SpaceAfterOpeningQuoteRegex = new(
        @"(“|’’|’)\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SpaceBeforeClosingQuoteRegex = new(
        @"\s+(”|‘‘|‘)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MissingSpaceBeforeOpeningQuoteRegex = new(
        @"(?<=[\p{L}\p{M}\p{N},،:؛;.!?؟۔])(“|’’|’)(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MissingSpaceAfterClosingQuoteRegex = new(
        @"(”|‘‘|‘)(?=[\p{L}\p{M}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex QuestionOrExclamationClosingQuoteThenFullStopRegex = new(
        @"([!?؟])(”|‘‘|‘)[.۔]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ClosingQuoteThenFullStopRegex = new(
        @"(?<=[\p{L}\p{M}\p{N}])(”|‘‘|‘)[.۔]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ArabicSpaceAfterOpeningQuoteRegex = new(
        @"(”|’’|’)\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ArabicSpaceBeforeClosingQuoteRegex = new(
        @"\s+(“|‘‘|‘)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ArabicMissingSpaceBeforeOpeningQuoteRegex = new(
        @"(?<=[\p{L}\p{M}\p{N},،:؛;.!?؟۔])(”|’’|’)(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ArabicMissingSpaceAfterClosingQuoteRegex = new(
        @"(“|‘‘|‘)(?=[\p{L}\p{M}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ArabicQuestionOrExclamationClosingQuoteThenFullStopRegex = new(
        @"([!?؟])(“|‘‘|‘)[.۔]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ArabicClosingQuoteThenFullStopRegex = new(
        @"(?<=[\p{L}\p{M}\p{N}])(“|‘‘|‘)[.۔]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeCommonSpacing(string value)
    {
        return NormalizeDirectionalQuoteSpacing(NormalizeCommonSpacingWithoutQuoteDirection(value));
    }

    private static string NormalizeCommonSpacingWithoutQuoteDirection(string value)
    {
        var result = NormalizeUsfmVerseMarkerPunctuation(value);
        result = RedundantFullStopAfterQuestionOrExclamationRegex.Replace(result, "$1");
        result = SpaceBeforePunctuationRegex.Replace(result, "$1");
        result = NormalizeUsfmVerseMarkerPunctuation(result);
        result = SpaceAfterOpeningParenthesisRegex.Replace(result, "(");
        result = SpaceBeforeClosingParenthesisRegex.Replace(result, ")");
        result = MissingSpaceBeforeOpeningParenthesisRegex.Replace(result, " (");
        result = MissingSpaceAfterExclamationOrQuestionRegex.Replace(result, "$1 ");
        return result;
    }

    public static string NormalizeArabicDerivedSpacing(string value)
    {
        return NormalizeArabicDerivedQuoteSpacing(NormalizeCommonSpacingWithoutQuoteDirection(value));
    }

    public static string NormalizeDirectionalQuoteSpacing(string value)
    {
        var result = MixedSingleQuotePairOpeningRegex.Replace(value, "“");
        result = MixedSingleQuotePairClosingRegex.Replace(result, "”");
        result = result.Replace("’’", "“").Replace("‘‘", "”");
        result = SpaceAfterOpeningQuoteRegex.Replace(result, "$1");
        result = SpaceBeforeClosingQuoteRegex.Replace(result, "$1");
        result = MissingSpaceBeforeOpeningQuoteRegex.Replace(result, " $1");
        result = MissingSpaceAfterClosingQuoteRegex.Replace(result, "$1 ");
        result = QuestionOrExclamationClosingQuoteThenFullStopRegex.Replace(result, "$1$2");
        return ClosingQuoteThenFullStopRegex.Replace(result, "۔$1");
    }

    public static string NormalizeArabicDerivedQuoteSpacing(string value)
    {
        var result = ConvertStraightArabicDerivedQuotes(value);
        result = MixedSingleQuotePairOpeningRegex.Replace(result, "”");
        result = MixedSingleQuotePairClosingRegex.Replace(result, "“");
        result = result.Replace("’’", "”").Replace("‘‘", "“");
        result = ArabicSpaceAfterOpeningQuoteRegex.Replace(result, "$1");
        result = ArabicSpaceBeforeClosingQuoteRegex.Replace(result, "$1");
        result = ArabicMissingSpaceBeforeOpeningQuoteRegex.Replace(result, " $1");
        result = ArabicMissingSpaceAfterClosingQuoteRegex.Replace(result, "$1 ");
        result = ArabicQuestionOrExclamationClosingQuoteThenFullStopRegex.Replace(result, "$1$2");
        return ArabicClosingQuoteThenFullStopRegex.Replace(result, "۔$1");
    }

    private static string ConvertStraightArabicDerivedQuotes(string value)
    {
        if (!value.Contains('"') && !value.Contains('\''))
        {
            return value;
        }

        var result = new System.Text.StringBuilder(value.Length);
        var doubleQuoteOpen = false;
        var singleQuoteOpen = false;

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch == '"')
            {
                result.Append(doubleQuoteOpen ? '“' : '”');
                doubleQuoteOpen = !doubleQuoteOpen;
                continue;
            }

            if (ch == '\'' && ShouldConvertStraightSingleQuote(value, index))
            {
                result.Append(singleQuoteOpen ? '‘' : '’');
                singleQuoteOpen = !singleQuoteOpen;
                continue;
            }

            result.Append(ch);
        }

        return result.ToString();
    }

    private static bool ShouldConvertStraightSingleQuote(string value, int quoteIndex)
    {
        var previous = quoteIndex > 0 ? value[quoteIndex - 1] : '\0';
        var next = quoteIndex + 1 < value.Length ? value[quoteIndex + 1] : '\0';
        return !(IsAsciiWordChar(previous) && IsAsciiWordChar(next));
    }

    private static bool IsAsciiWordChar(char value)
    {
        return value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_';
    }

    public static string NormalizeUsfmVerseMarkerPunctuation(string value)
    {
        return UsfmVerseMarkerWithTrailingPunctuationRegex.Replace(value, "$1 ");
    }
}
