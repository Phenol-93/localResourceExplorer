using Serilog;

namespace LocalResourceExplorer.Services;

public static class AppLog
{
    private const int MaxValueLength = 500;

    public static void ScanWarning(Exception exception, string message, string? path = null)
    {
#if DEBUG
        Log.Warning(exception, "{Message}. Path: {Path}", message, Truncate(path));
#else
        Log.Warning(
            "{Message}. Path: {Path}. ErrorType: {ErrorType}. Error: {ErrorMessage}",
            message,
            Truncate(path),
            exception.GetType().Name,
            Truncate(exception.Message));
#endif
    }

    public static void DatabaseError(Exception exception, string message, string? path = null)
    {
#if DEBUG
        Log.Error(exception, "{Message}. Path: {Path}", message, Truncate(path));
#else
        Log.Error(
            "{Message}. Path: {Path}. ErrorType: {ErrorType}. Error: {ErrorMessage}",
            message,
            Truncate(path),
            exception.GetType().Name,
            Truncate(exception.Message));
#endif
    }

    public static void AiError(Exception exception, string message)
    {
#if DEBUG
        Log.Error(exception, "{Message}", message);
#else
        Log.Error(
            "{Message}. ErrorType: {ErrorType}. Error: {ErrorMessage}",
            message,
            exception.GetType().Name,
            Truncate(exception.Message));
#endif
    }

    public static void MigrationInfo(string message, params object?[] propertyValues)
    {
        Log.Information(message, propertyValues);
    }

    public static void MigrationWarning(string message, params object?[] propertyValues)
    {
        Log.Warning(message, propertyValues);
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= MaxValueLength)
        {
            return value;
        }

        return value[..MaxValueLength] + "...";
    }
}
