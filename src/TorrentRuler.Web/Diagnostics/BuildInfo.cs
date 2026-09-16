using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TorrentRuler.Web.Diagnostics;

/// <summary>
/// Identifies the running build for the Settings "Info" card. The Docker image has no .git
/// directory and no git binary (see .dockerignore), so the primary source is the
/// TORRENTRULER_GIT_SHA / TORRENTRULER_GIT_BRANCH env vars a `docker build --build-arg` bakes in
/// (see Dockerfile, docker-compose.yml). Running via `dotnet run` from a git checkout has neither
/// env var set, so it falls back to asking git directly; that call is best-effort and silently
/// yields "unknown" wherever git (or a build-arg) isn't available.
/// </summary>
public static class BuildInfo
{
    public static string GitBranch { get; } =
        NullIfUnset(Environment.GetEnvironmentVariable("TORRENTRULER_GIT_BRANCH"))
        ?? TryGit("branch --show-current")
        ?? "unknown";

    public static string GitCommit { get; } =
        NullIfUnset(Environment.GetEnvironmentVariable("TORRENTRULER_GIT_SHA"))
        ?? TryGit("rev-parse --short HEAD")
        ?? "unknown";

    /// <summary>Approximated from the entry assembly's file timestamp -- accurate enough for
    /// "when was this image built" without adding a dedicated build step.</summary>
    public static DateTime? BuildTimeUtc { get; } = TryGetAssemblyTimestamp();

    public static string RuntimeVersion { get; } = RuntimeInformation.FrameworkDescription;

    private static string? NullIfUnset(string? value) =>
        string.IsNullOrWhiteSpace(value) || value == "unknown" ? null : value;

    private static string? TryGit(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(2000))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch
        {
            // git not installed, not a repo, or anything else -- this is best-effort only.
            return null;
        }
    }

    private static DateTime? TryGetAssemblyTimestamp()
    {
        try
        {
            var location = Assembly.GetExecutingAssembly().Location;
            return string.IsNullOrEmpty(location) ? null : File.GetLastWriteTimeUtc(location);
        }
        catch
        {
            return null;
        }
    }
}
