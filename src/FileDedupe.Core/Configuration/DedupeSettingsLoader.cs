using FileDedupe.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace FileDedupe.Core.Configuration;

/// <summary>
/// Loads <see cref="DedupeSettings"/> from appsettings.json beside the executable.
/// A missing or malformed file is not fatal - the defaults are usable.
/// </summary>
public static class DedupeSettingsLoader
{
    public const string FileName = "appsettings.json";

    public static DedupeSettings Load(string? basePath = null)
    {
        basePath ??= AppContext.BaseDirectory;

        var settings = new DedupeSettings();

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile(FileName, optional: true, reloadOnChange: false)
                .Build();

            configuration.GetSection(DedupeSettings.SectionName).Bind(settings);
        }
        catch (Exception)
        {
            // Deliberately swallowed: configuration problems must not stop the app from
            // starting, and surfacing them needs a logger that is not built yet.
            settings = new DedupeSettings();
        }

        if (settings.MinimumFileSizeBytes < 0)
        {
            settings.MinimumFileSizeBytes = 0;
        }

        settings.DatabasePath = Resolve(settings.DatabasePath, basePath, "filededupe.db");
        settings.ErrorLogPath = Resolve(settings.ErrorLogPath, basePath, Path.Combine("logs", "filededupe-errors.json"));

        return settings;
    }

    private static string Resolve(string configured, string basePath, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return Path.IsPathRooted(value) ? value : Path.Combine(basePath, value);
    }
}
