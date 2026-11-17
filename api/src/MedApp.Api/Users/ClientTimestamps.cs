namespace MedApp.Api.Users;

/// <summary>
/// Validation for timestamps set by the phone. Readings and messages can be queued offline and synced later, so
/// the API keeps the phone's time, but rejects times in the future (beyond clock skew) or implausibly old.
/// </summary>
internal static class ClientTimestamps
{
    public static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(366);

    /// <summary>An error message, or null when <paramref name="value"/> is acceptable.</summary>
    public static string? Validate(DateTimeOffset value, DateTimeOffset now)
    {
        if (value > now + AllowedClockSkew)
        {
            return "Must not be in the future.";
        }
        if (value < now - MaxAge)
        {
            return "Must be within the last 366 days.";
        }
        return null;
    }
}
