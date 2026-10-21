using System.Reflection;

namespace AgentSpeed.App;

internal static class AppPaths
{
    public const string ProductFolder = "AgentSpeed";
    public const string RepositoryUrl = "https://github.com/zoefix/agent-speed";
    public const string LicenseUrl = "https://github.com/zoefix/agent-speed/blob/main/LICENSE";
    public const string ReleasesUrl = "https://github.com/zoefix/agent-speed/releases/latest";

    public static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductFolder, "settings.json");

    public static string LogsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductFolder, "logs");

    public static string? ExecutablePath => Environment.ProcessPath;

    public static string Version
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(informational))
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            }

            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
    }
}
