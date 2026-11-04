using System.Text.RegularExpressions;

namespace ChallengeBot.Logic;

/// <summary>Результат разбора подписи к видео.</summary>
public record CaptionInfo(int? Wellbeing, bool IsGentle);

/// <summary>Достаёт из подписи самочувствие и слово «щадящий».</summary>
public static class CaptionParser
{
    // «Самочувствие 7/10», «самочувствие: 7 / 10»
    private static readonly Regex WellbeingRegex = new(
        @"самочувствие\s*[:\-]?\s*(\d{1,2})\s*/\s*10",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // «щадящий», «щадящем режиме», «щадящая» — достаточно корня
    private static readonly Regex GentleRegex = new(
        @"щадящ",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static CaptionInfo Parse(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
            return new CaptionInfo(null, false);

        int? wellbeing = null;
        var match = WellbeingRegex.Match(caption);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var value) && value is >= 0 and <= 10)
            wellbeing = value;

        return new CaptionInfo(wellbeing, GentleRegex.IsMatch(caption));
    }
}
