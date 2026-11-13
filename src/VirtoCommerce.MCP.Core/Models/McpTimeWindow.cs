using System;
using ModelContextProtocol;

namespace VirtoCommerce.MCP.Core.Models;

// The period a tool reads, from the arguments people use: "the last 24 hours" (hours), "the last 7 days" (days), or
// "since Monday" (since). It always ends now, has a default when the agent gives nothing, and is capped, so one call
// cannot scan years of data. Serialized as from, to, hours and was_capped, so the answer says what it covered.
public sealed class McpTimeWindow
{
    private McpTimeWindow(DateTime from, DateTime to, bool wasCapped)
    {
        From = from;
        To = to;
        WasCapped = wasCapped;
    }

    // UTC
    public DateTime From { get; }

    // UTC: now.
    public DateTime To { get; }

    public double Hours => Math.Round((To - From).TotalHours, 2);

    // The agent asked for more than the cap, and the window was shortened to it.
    public bool WasCapped { get; }

    public bool Contains(DateTime value)
    {
        var utc = ToUtc(value);

        return utc >= From && utc <= To;
    }

    // The last hours: defaultHours when hours is null, at most maxHours.
    public static McpTimeWindow LastHours(int? hours, TimeProvider timeProvider, int defaultHours = 24, int maxHours = 24 * 30)
    {
        return Last(hours, TimeSpan.FromHours(1), defaultHours, maxHours, "hours", timeProvider);
    }

    // The last days: defaultDays when days is null, at most maxDays.
    public static McpTimeWindow LastDays(int? days, TimeProvider timeProvider, int defaultDays = 7, int maxDays = 365)
    {
        return Last(days, TimeSpan.FromDays(1), defaultDays, maxDays, "days", timeProvider);
    }

    // From a moment until now: the default length when since is null, at most maxLength back. A since without a kind is
    // read as UTC; a since in the future is refused with a message the agent can pass on.
    public static McpTimeWindow Since(DateTime? since, TimeProvider timeProvider, TimeSpan defaultLength, TimeSpan maxLength)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (defaultLength <= TimeSpan.Zero || maxLength < defaultLength)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultLength), "The default length must be positive and not above the maximum.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (since == null)
        {
            return new McpTimeWindow(now - defaultLength, now, wasCapped: false);
        }

        var from = ToUtc(since.Value);
        if (from > now)
        {
            throw new McpException($"'since' ({from:u}) is in the future; give a moment in the past.");
        }

        var earliest = now - maxLength;

        return from < earliest
            ? new McpTimeWindow(earliest, now, wasCapped: true)
            : new McpTimeWindow(from, now, wasCapped: false);
    }

    private static McpTimeWindow Last(int? count, TimeSpan unit, int defaultCount, int maxCount, string argumentName, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (defaultCount < 1 || maxCount < defaultCount)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultCount), "The default must be at least 1 and not above the maximum.");
        }

        if (count < 1)
        {
            throw new McpException($"'{argumentName}' must be at least 1; omit it for the last {defaultCount}.");
        }

        var value = count ?? defaultCount;
        var wasCapped = value > maxCount;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return new McpTimeWindow(now - (unit * Math.Min(value, maxCount)), now, wasCapped);
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
