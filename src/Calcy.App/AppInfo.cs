using System.Reflection;

namespace Calcy.App;

/// <summary>Version, commit and licence of this build, as stamped in at build time (see build/package.ps1).</summary>
internal static class AppInfo
{
    private static readonly string Informational =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>The product version, for example 0.2.0 or 0.2.0-pre.14.</summary>
    public static string Version => Informational.Split('+')[0];

    /// <summary>The short commit the build came from, when known.</summary>
    public static string? Commit
    {
        get
        {
            int plus = Informational.IndexOf('+');
            if (plus < 0 || plus == Informational.Length - 1) return null;
            var sha = Informational[(plus + 1)..];
            return sha.Length > 7 ? sha[..7] : sha;
        }
    }

    /// <summary>"Calcy 0.2.0-pre.14 (abc1234)".</summary>
    public static string Description => Commit is null ? $"Calcy {Version}" : $"Calcy {Version} ({Commit})";

    public const string Copyright = "Copyright (c) 2026 Alexander Miglis";
    public const string License = "MIT License";
}