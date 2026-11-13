using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace FileDedupe.Core.Logging;

/// <summary>
/// Builds the Serilog error logger. Output is newline-delimited JSON (one object per
/// event) so the log stays machine-readable and can be opened incrementally rather
/// than needing a whole well-formed document.
/// </summary>
public static class ErrorLoggerFactory
{
    public static ILogger Create(string errorLogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorLogPath);

        var fullPath = Path.GetFullPath(errorLogPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new LoggerConfiguration()
            .MinimumLevel.Error()
            .WriteTo.File(
                new CompactJsonFormatter(),
                fullPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                restrictedToMinimumLevel: LogEventLevel.Error,
                shared: true)
            .CreateLogger();
    }
}
