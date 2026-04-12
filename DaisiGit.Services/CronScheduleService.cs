using System.Text.RegularExpressions;

namespace DaisiGit.Services;

/// <summary>
/// Parses schedule expressions and computes next run times.
/// Supports standard 5-field cron (minute hour day month weekday)
/// and human-friendly intervals stored in TriggerFilters["schedule"].
///
/// Friendly formats:
///   "every 5m"          → every 5 minutes
///   "every 2h"          → every 2 hours
///   "every 30s"         → every 30 seconds (min 30s)
///   "daily at 08:00"    → cron 0 8 * * *
///   "hourly"            → cron 0 * * * *
///
/// Cron format:
///   "*/15 * * * *"      → every 15 minutes
///   "0 9 * * 1-5"       → 9 AM weekdays
/// </summary>
public static class CronScheduleService
{
    private static readonly Regex IntervalRegex = new(
        @"^every\s+(\d+)\s*(s|sec|seconds?|m|min|minutes?|h|hr|hours?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DailyAtRegex = new(
        @"^daily\s+at\s+(\d{1,2}):(\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Computes the next run time from the given schedule expression.
    /// </summary>
    public static DateTime? GetNextRunUtc(string? schedule, DateTime? afterUtc = null)
    {
        if (string.IsNullOrWhiteSpace(schedule))
            return null;

        var after = afterUtc ?? DateTime.UtcNow;
        schedule = schedule.Trim();

        // Friendly: "hourly"
        if (schedule.Equals("hourly", StringComparison.OrdinalIgnoreCase))
            return GetNextCronRunUtc("0 * * * *", after);

        // Friendly: "daily at HH:mm"
        var dailyMatch = DailyAtRegex.Match(schedule);
        if (dailyMatch.Success)
        {
            var hour = int.Parse(dailyMatch.Groups[1].Value);
            var minute = int.Parse(dailyMatch.Groups[2].Value);
            return GetNextCronRunUtc($"{minute} {hour} * * *", after);
        }

        // Friendly: "every Ns/Nm/Nh"
        var intervalMatch = IntervalRegex.Match(schedule);
        if (intervalMatch.Success)
        {
            var amount = int.Parse(intervalMatch.Groups[1].Value);
            var unit = intervalMatch.Groups[2].Value.ToLowerInvariant();
            var interval = unit switch
            {
                "s" or "sec" or "second" or "seconds" => TimeSpan.FromSeconds(Math.Max(30, amount)),
                "m" or "min" or "minute" or "minutes" => TimeSpan.FromMinutes(amount),
                "h" or "hr" or "hour" or "hours" => TimeSpan.FromHours(amount),
                _ => TimeSpan.FromMinutes(amount)
            };
            if (interval < TimeSpan.FromSeconds(30))
                interval = TimeSpan.FromSeconds(30);
            return after + interval;
        }

        // Standard cron expression (5 fields)
        return GetNextCronRunUtc(schedule, after);
    }

    /// <summary>
    /// Validates a schedule expression. Returns null if valid, or an error message.
    /// </summary>
    public static string? Validate(string? schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
            return "Schedule expression is required.";

        schedule = schedule.Trim();

        if (schedule.Equals("hourly", StringComparison.OrdinalIgnoreCase))
            return null;

        if (DailyAtRegex.IsMatch(schedule))
            return null;

        if (IntervalRegex.IsMatch(schedule))
            return null;

        // Try to parse as cron
        var parts = schedule.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return "Cron expression must have 5 fields: minute hour day month weekday.";

        try
        {
            GetNextCronRunUtc(schedule, DateTime.UtcNow);
            return null;
        }
        catch
        {
            return "Invalid cron expression.";
        }
    }

    /// <summary>
    /// Returns a human-readable description of the schedule.
    /// </summary>
    public static string Describe(string? schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
            return "No schedule";

        schedule = schedule.Trim();

        if (schedule.Equals("hourly", StringComparison.OrdinalIgnoreCase))
            return "Every hour";

        var dailyMatch = DailyAtRegex.Match(schedule);
        if (dailyMatch.Success)
            return $"Daily at {dailyMatch.Groups[1].Value}:{dailyMatch.Groups[2].Value} UTC";

        var intervalMatch = IntervalRegex.Match(schedule);
        if (intervalMatch.Success)
        {
            var amount = int.Parse(intervalMatch.Groups[1].Value);
            var unit = intervalMatch.Groups[2].Value.ToLowerInvariant();
            var friendly = unit switch
            {
                "s" or "sec" or "second" or "seconds" => $"{Math.Max(30, amount)} seconds",
                "m" or "min" or "minute" or "minutes" => amount == 1 ? "minute" : $"{amount} minutes",
                "h" or "hr" or "hour" or "hours" => amount == 1 ? "hour" : $"{amount} hours",
                _ => $"{amount} minutes"
            };
            return $"Every {friendly}";
        }

        return $"Cron: {schedule}";
    }

    // ── Minimal cron evaluator (5-field: min hour dom month dow) ──

    private static DateTime GetNextCronRunUtc(string cron, DateTime after)
    {
        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            throw new FormatException("Cron expression must have 5 fields.");

        var minutes = ParseField(parts[0], 0, 59);
        var hours = ParseField(parts[1], 0, 23);
        var doms = ParseField(parts[2], 1, 31);
        var months = ParseField(parts[3], 1, 12);
        var dows = ParseField(parts[4], 0, 6); // 0=Sunday

        // Start from the next minute after 'after'
        var candidate = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(1);

        // Search up to 2 years ahead
        var limit = after.AddYears(2);
        while (candidate < limit)
        {
            if (months.Contains(candidate.Month)
                && doms.Contains(candidate.Day)
                && dows.Contains((int)candidate.DayOfWeek)
                && hours.Contains(candidate.Hour)
                && minutes.Contains(candidate.Minute))
            {
                return candidate;
            }

            candidate = candidate.AddMinutes(1);

            // Optimization: skip ahead when month doesn't match
            if (!months.Contains(candidate.Month))
            {
                candidate = NextMonth(candidate);
                continue;
            }

            // Skip ahead when day doesn't match
            if (!doms.Contains(candidate.Day) || !dows.Contains((int)candidate.DayOfWeek))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }

            // Skip ahead when hour doesn't match
            if (!hours.Contains(candidate.Hour))
            {
                candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day,
                    candidate.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
            }
        }

        throw new InvalidOperationException("Could not find next cron run within 2 years.");
    }

    private static DateTime NextMonth(DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
    }

    private static HashSet<int> ParseField(string field, int min, int max)
    {
        var result = new HashSet<int>();

        foreach (var part in field.Split(','))
        {
            var trimmed = part.Trim();

            // */N or N-M/N
            if (trimmed.Contains('/'))
            {
                var slashParts = trimmed.Split('/');
                var step = int.Parse(slashParts[1]);
                var (rangeMin, rangeMax) = ParseRange(slashParts[0], min, max);
                for (var i = rangeMin; i <= rangeMax; i += step)
                    result.Add(i);
            }
            // N-M
            else if (trimmed.Contains('-'))
            {
                var (rangeMin, rangeMax) = ParseRange(trimmed, min, max);
                for (var i = rangeMin; i <= rangeMax; i++)
                    result.Add(i);
            }
            // *
            else if (trimmed == "*")
            {
                for (var i = min; i <= max; i++)
                    result.Add(i);
            }
            // Single value
            else
            {
                result.Add(int.Parse(trimmed));
            }
        }

        return result;
    }

    private static (int Min, int Max) ParseRange(string range, int fieldMin, int fieldMax)
    {
        if (range == "*")
            return (fieldMin, fieldMax);

        var parts = range.Split('-');
        if (parts.Length == 2)
            return (int.Parse(parts[0]), int.Parse(parts[1]));

        var val = int.Parse(range);
        return (val, val);
    }
}
