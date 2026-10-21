using System.Reflection;

namespace AgentSpeed.Presentation.Tests.TestSupport;

internal static class RepositoryPaths
{
    private const string SharedStringsRelativePath = "spec/strings.json";

    /// <summary>
    /// Finds spec/strings.json by walking up from the test output folder; falls back to the repository root recorded
    /// at build time, because the output folder may live outside the repository (custom artifacts path).
    /// </summary>
    public static string SharedStringsFile()
    {
        foreach (var start in StartDirectories())
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, SharedStringsRelativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("Could not locate spec/strings.json from the test output folder.");
    }

    private static IEnumerable<string> StartDirectories()
    {
        yield return AppContext.BaseDirectory;

        var recorded = typeof(RepositoryPaths).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;
        if (!string.IsNullOrEmpty(recorded) && Directory.Exists(recorded))
        {
            yield return recorded;
        }
    }
}
