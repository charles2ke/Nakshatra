namespace Nakshatra.Shared;

/// <summary>
/// Helpers for writing caller supplied values (cache keys, topic names, ids) to the log without
/// letting newlines or control characters forge extra log entries.
/// </summary>
public static class LogSanitizer
{
    private const int MaxLength = 256;

    /// <summary>Strips control characters and truncates the value so it stays a single log entry.</summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var span = value.Length > MaxLength ? value.AsSpan(0, MaxLength) : value.AsSpan();
        var buffer = new char[span.Length];
        var written = 0;

        foreach (var ch in span)
        {
            buffer[written++] = char.IsControl(ch) ? '_' : ch;
        }

        return new string(buffer, 0, written);
    }
}
