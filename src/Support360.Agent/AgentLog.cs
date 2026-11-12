namespace Support360.Agent;

internal static class AgentLog
{
    private static readonly object Gate = new();

    public static void Info(string message, params (string Key, object? Value)[] fields) => Write("INF", message, fields);

    public static void Warn(string message, params (string Key, object? Value)[] fields) => Write("WRN", message, fields);

    public static void Error(string message, params (string Key, object? Value)[] fields) => Write("ERR", message, fields);

    private static void Write(string level, string message, (string Key, object? Value)[] fields)
    {
        var suffix = fields.Length == 0
            ? string.Empty
            : " " + string.Join(" ", fields.Select(FormatField));
        lock (Gate)
        {
            Console.Error.WriteLine($"{DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")} {level} {message}{suffix}");
        }
    }

    private static string FormatField((string Key, object? Value) field)
    {
        var value = field.Value switch
        {
            null => string.Empty,
            string text => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
            _ => Convert.ToString(field.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
        };
        return $"{field.Key}={value}";
    }
}
